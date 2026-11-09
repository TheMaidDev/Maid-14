using System.Security.Cryptography;
using System.Text;
using Content.Shared._Maid.PlayerMusic;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Maid.PlayerMusic;

/// <summary>
/// Lightweight replacement for the "upload music to everyone" approach:
/// songs are validated and stored once, and sent only to the single client
/// that is standing near a boombox playing that song.
/// </summary>
public sealed class PlayerMusicSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const int MaxNameLength = 40;

    private sealed record StoredSong(string Id, string Name, byte[] Data, NetUserId Owner);

    private readonly Dictionary<string, StoredSong> _songs = new();
    private readonly Dictionary<NetUserId, HashSet<string>> _owned = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastUpload = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastRequest = new();
    private long _totalBytes;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PlayerMusicUploadEvent>(OnUpload);
        SubscribeNetworkEvent<PlayerMusicRequestFileEvent>(OnRequestFile);

        SubscribeLocalEvent<PlayerMusicBoomboxComponent, PlayerMusicPlayMessage>(OnPlay);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, PlayerMusicStopMessage>(OnStop);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, PlayerMusicRepeatMessage>(OnRepeat);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, EntRemovedFromContainerMessage>(OnRemoved);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _songs.Clear();
        _owned.Clear();
        _lastUpload.Clear();
        _lastRequest.Clear();
        _totalBytes = 0;
    }

    #region Boombox controls

    private void OnPlay(Entity<PlayerMusicBoomboxComponent> ent, ref PlayerMusicPlayMessage args)
    {
        if (!_cfg.GetCVar(PlayerMusicCVars.Enabled))
            return;

        if (!TryGetCassette(ent, out var cassette))
            return;

        var tracks = cassette.Comp.Tracks;
        if (args.Track < 0 || args.Track >= tracks.Count)
            return;

        var track = tracks[args.Track];
        if (!_songs.ContainsKey(track.Id))
        {
            _popup.PopupEntity(Loc.GetString("maid-music-song-lost"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        ent.Comp.SongId = track.Id;
        ent.Comp.SongName = track.Name;
        ent.Comp.StartedAt = _timing.CurTime;
        Dirty(ent);
        _appearance.SetData(ent, PlayerMusicVisuals.Playing, true);
    }

    private void OnStop(Entity<PlayerMusicBoomboxComponent> ent, ref PlayerMusicStopMessage args)
    {
        StopPlaying(ent);
    }

    private void OnRepeat(Entity<PlayerMusicBoomboxComponent> ent, ref PlayerMusicRepeatMessage args)
    {
        ent.Comp.Repeat = args.Repeat;
        Dirty(ent);
    }

    private void StopPlaying(Entity<PlayerMusicBoomboxComponent> ent)
    {
        ent.Comp.StartedAt = null;
        Dirty(ent);
        _appearance.SetData(ent, PlayerMusicVisuals.Playing, false);
    }

    private void OnInserted(Entity<PlayerMusicBoomboxComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != PlayerMusicBoomboxComponent.CassetteSlotId)
            return;

        UpdateCassetteInfo(ent);
    }

    private void OnRemoved(Entity<PlayerMusicBoomboxComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != PlayerMusicBoomboxComponent.CassetteSlotId)
            return;

        ent.Comp.HasCassette = false;
        ent.Comp.CassetteMaxTracks = 0;
        ent.Comp.CassetteTracks = new List<PlayerMusicTrack>();
        ent.Comp.StartedAt = null;
        Dirty(ent);
        _appearance.SetData(ent, PlayerMusicVisuals.Playing, false);
    }

    private void UpdateCassetteInfo(Entity<PlayerMusicBoomboxComponent> ent)
    {
        if (TryGetCassette(ent, out var cassette))
        {
            ent.Comp.HasCassette = true;
            ent.Comp.CassetteMaxTracks = cassette.Comp.MaxTracks;
            ent.Comp.CassetteTracks = new List<PlayerMusicTrack>(cassette.Comp.Tracks);
        }
        else
        {
            ent.Comp.HasCassette = false;
            ent.Comp.CassetteMaxTracks = 0;
            ent.Comp.CassetteTracks = new List<PlayerMusicTrack>();
        }

        Dirty(ent);
    }

    private bool TryGetCassette(EntityUid boombox, out Entity<PlayerMusicCassetteComponent> cassette)
    {
        cassette = default;

        if (!_slots.TryGetSlot(boombox, PlayerMusicBoomboxComponent.CassetteSlotId, out var slot) ||
            slot.Item is not { } item ||
            !TryComp(item, out PlayerMusicCassetteComponent? comp))
        {
            return false;
        }

        cassette = (item, comp);
        return true;
    }

    #endregion

    #region Upload

    private void OnUpload(PlayerMusicUploadEvent ev, EntitySessionEventArgs args)
    {
        if (!_cfg.GetCVar(PlayerMusicCVars.Enabled))
            return;

        var session = args.SenderSession;
        if (session.AttachedEntity is not { } user)
            return;

        var boomboxUid = GetEntity(ev.Boombox);
        if (!TryComp(boomboxUid, out PlayerMusicBoomboxComponent? boomboxComp))
            return;

        var boombox = new Entity<PlayerMusicBoomboxComponent>(boomboxUid, boomboxComp);

        if (!_interaction.InRangeUnobstructed(user, boomboxUid))
        {
            Deny(boomboxUid, user, "maid-music-too-far");
            return;
        }

        if (!TryGetCassette(boomboxUid, out var cassette))
        {
            Deny(boomboxUid, user, "maid-music-no-cassette");
            return;
        }

        if (cassette.Comp.Tracks.Count >= cassette.Comp.MaxTracks)
        {
            Deny(boomboxUid, user, "maid-music-cassette-full");
            return;
        }

        var data = ev.Data;
        var maxBytes = _cfg.GetCVar(PlayerMusicCVars.MaxSongBytes);
        if (data.Length == 0 || data.Length > maxBytes)
        {
            Deny(boomboxUid, user, "maid-music-too-big", ("max", FormatMb(maxBytes)));
            return;
        }

        var channels = GetVorbisChannels(data);
        if (channels is < 1 or > 2)
        {
            Deny(boomboxUid, user, "maid-music-not-ogg");
            return;
        }

        var name = SanitizeName(ev.Name);
        if (name.Length == 0)
        {
            Deny(boomboxUid, user, "maid-music-bad-name");
            return;
        }

        var now = _timing.CurTime;
        var userId = session.UserId;
        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(PlayerMusicCVars.UploadCooldown));
        if (_lastUpload.TryGetValue(userId, out var last) && now - last < cooldown)
        {
            Deny(boomboxUid, user, "maid-music-cooldown");
            return;
        }

        var id = Convert.ToHexString(SHA256.HashData(data))[..32];

        if (!_songs.ContainsKey(id))
        {
            var owned = _owned.GetOrNew(userId);
            if (owned.Count >= _cfg.GetCVar(PlayerMusicCVars.MaxSongsPerPlayer))
            {
                Deny(boomboxUid, user, "maid-music-quota");
                return;
            }

            if (_totalBytes + data.Length > _cfg.GetCVar(PlayerMusicCVars.MaxTotalBytes))
            {
                Deny(boomboxUid, user, "maid-music-storage-full");
                return;
            }

            _songs[id] = new StoredSong(id, name, data, userId);
            _totalBytes += data.Length;
            owned.Add(id);
        }

        _lastUpload[userId] = now;

        cassette.Comp.Tracks.Add(new PlayerMusicTrack(id, name));
        Dirty(cassette);
        UpdateCassetteInfo(boombox);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} recorded song '{name}' ({data.Length} bytes, id {id}) onto a cassette in {ToPrettyString(boomboxUid)}");

        var message = channels == 2 ? "maid-music-recorded-stereo" : "maid-music-recorded";
        _popup.PopupEntity(Loc.GetString(message, ("name", name)), boomboxUid, user);
    }

    private void Deny(EntityUid boombox, EntityUid user, string locKey, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(locKey, args), boombox, user, PopupType.SmallCaution);
    }

    private static string FormatMb(int bytes)
    {
        return (bytes / 1_000_000f).ToString("0.##");
    }

    /// <summary>
    /// Reads the channel count from the Ogg Vorbis identification header.
    /// Returns 0 if the data is not an Ogg Vorbis file (the engine can only play Vorbis).
    /// </summary>
    private static int GetVorbisChannels(byte[] d)
    {
        if (d.Length < 64 ||
            d[0] != (byte) 'O' || d[1] != (byte) 'g' || d[2] != (byte) 'g' || d[3] != (byte) 'S')
        {
            return 0;
        }

        // Ogg page header is 27 bytes, followed by the segment table
        var start = 27 + d[26];
        if (start + 12 > d.Length)
            return 0;

        if (d[start] != 1 ||
            d[start + 1] != (byte) 'v' || d[start + 2] != (byte) 'o' || d[start + 3] != (byte) 'r' ||
            d[start + 4] != (byte) 'b' || d[start + 5] != (byte) 'i' || d[start + 6] != (byte) 's')
        {
            return 0;
        }

        // 7 bytes "\x01vorbis", 4 bytes version, 1 byte channel count
        return d[start + 11];
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsControl(c))
                continue;

            sb.Append(c);

            if (sb.Length >= MaxNameLength)
                break;
        }

        return sb.ToString().Trim();
    }

    #endregion

    #region Download on demand

    private void OnRequestFile(PlayerMusicRequestFileEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (session.AttachedEntity is not { } user)
            return;

        var now = _timing.CurTime;
        if (_lastRequest.TryGetValue(session.UserId, out var last) && now - last < TimeSpan.FromSeconds(1))
            return;

        _lastRequest[session.UserId] = now;

        var boombox = GetEntity(ev.Boombox);
        if (!TryComp(boombox, out PlayerMusicBoomboxComponent? comp) ||
            comp.StartedAt == null ||
            comp.SongId != ev.SongId)
        {
            return;
        }

        if (!_songs.TryGetValue(ev.SongId, out var song))
            return;

        var range = _cfg.GetCVar(PlayerMusicCVars.ListenRange);
        if (!_xform.InRange(Transform(user).Coordinates, Transform(boombox).Coordinates, range))
            return;

        // recordReplay: false, so replays don't get filled with audio files
        RaiseNetworkEvent(new PlayerMusicFileEvent(song.Id, song.Data), Filter.SinglePlayer(session), false);
    }

    #endregion
}
