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
using Robust.Shared.Enums;
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

    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(15);
    private const int ChunksPerTick = 4;

    private sealed record StoredSong(string Id, string Name, byte[] Data, NetUserId Owner, TimeSpan Duration);

    private sealed class PendingUpload(EntityUid boombox, string name, int total, TimeSpan now)
    {
        public readonly EntityUid Boombox = boombox;
        public readonly string Name = name;
        public readonly byte[] Buffer = new byte[total];
        public int Received;
        public int NextIndex;
        public TimeSpan LastActivity = now;
    }

    private sealed class Transfer(ICommonSession session, StoredSong song)
    {
        public readonly ICommonSession Session = session;
        public readonly StoredSong Song = song;
        public int Offset;
        public int Index;
    }

    private readonly Dictionary<string, StoredSong> _songs = new();
    private readonly Dictionary<NetUserId, HashSet<string>> _owned = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastUpload = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastRequest = new();
    private readonly Dictionary<NetUserId, PendingUpload> _pending = new();
    private readonly Dictionary<NetUserId, Transfer> _transfers = new();
    private TimeSpan _nextCheck;
    private long _totalBytes;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PlayerMusicUploadStartEvent>(OnUploadStart);
        SubscribeNetworkEvent<PlayerMusicUploadChunkEvent>(OnUploadChunk);
        SubscribeNetworkEvent<PlayerMusicRequestFileEvent>(OnRequestFile);
        SubscribeNetworkEvent<PlayerMusicCancelFileEvent>(OnCancelFile);

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
        _pending.Clear();
        _transfers.Clear();
        _totalBytes = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        SendChunks();

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + TimeSpan.FromSeconds(0.5);

        // Interrupted uploads must not hold memory forever
        List<NetUserId>? stale = null;
        foreach (var (user, upload) in _pending)
        {
            if (now - upload.LastActivity > UploadTimeout)
                (stale ??= new()).Add(user);
        }

        if (stale != null)
        {
            foreach (var user in stale)
            {
                _pending.Remove(user);
            }
        }

        // A song that is not repeated stops by itself when it ends
        var query = EntityQueryEnumerator<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.StartedAt is not { } started || comp.Repeat || comp.SongId == null)
                continue;

            if (!_songs.TryGetValue(comp.SongId, out var song) || now - started >= song.Duration + TimeSpan.FromSeconds(0.3))
                StopPlaying((uid, comp));
        }
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
        if (ent.Comp.Repeat == args.Repeat)
            return;

        // Repeat was turned off while looping: keep the position inside the current loop,
        // so the song finishes normally instead of jumping around.
        if (!args.Repeat &&
            ent.Comp.StartedAt is { } started &&
            ent.Comp.SongId is { } songId &&
            _songs.TryGetValue(songId, out var song) &&
            song.Duration > TimeSpan.Zero)
        {
            var now = _timing.CurTime;
            var position = TimeSpan.FromTicks(Math.Max(0, (now - started).Ticks) % song.Duration.Ticks);
            ent.Comp.StartedAt = now - position;
        }

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

    /// <summary>
    /// Checks that the player may record on the boombox right now. Shows the reason if not.
    /// </summary>
    private bool CanRecord(
        ICommonSession session,
        EntityUid boomboxUid,
        out EntityUid user,
        out Entity<PlayerMusicCassetteComponent> cassette)
    {
        cassette = default;
        user = default;

        if (!_cfg.GetCVar(PlayerMusicCVars.Enabled) || session.AttachedEntity is not { } attached)
            return false;

        user = attached;

        if (!HasComp<PlayerMusicBoomboxComponent>(boomboxUid))
            return false;

        if (!_interaction.InRangeUnobstructed(user, boomboxUid))
        {
            Deny(boomboxUid, user, "maid-music-too-far");
            return false;
        }

        if (!TryGetCassette(boomboxUid, out cassette))
        {
            Deny(boomboxUid, user, "maid-music-no-cassette");
            return false;
        }

        if (cassette.Comp.Tracks.Count >= cassette.Comp.MaxTracks)
        {
            Deny(boomboxUid, user, "maid-music-cassette-full");
            return false;
        }

        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(PlayerMusicCVars.UploadCooldown));
        if (_lastUpload.TryGetValue(session.UserId, out var last) && _timing.CurTime - last < cooldown)
        {
            Deny(boomboxUid, user, "maid-music-cooldown");
            return false;
        }

        return true;
    }

    private void OnUploadStart(PlayerMusicUploadStartEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        // A new start always cancels the previous unfinished upload
        _pending.Remove(session.UserId);

        var boomboxUid = GetEntity(ev.Boombox);
        if (!CanRecord(session, boomboxUid, out var user, out _))
            return;

        var maxBytes = _cfg.GetCVar(PlayerMusicCVars.MaxSongBytes);
        if (ev.TotalBytes <= 0 || ev.TotalBytes > maxBytes)
        {
            Deny(boomboxUid, user, "maid-music-too-big", ("max", FormatMb(maxBytes)));
            return;
        }

        var name = SanitizeName(ev.Name);
        if (name.Length == 0)
        {
            Deny(boomboxUid, user, "maid-music-bad-name");
            return;
        }

        _pending[session.UserId] = new PendingUpload(boomboxUid, name, ev.TotalBytes, _timing.CurTime);
    }

    private void OnUploadChunk(PlayerMusicUploadChunkEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_pending.TryGetValue(session.UserId, out var upload))
            return;

        var length = ev.Data.Length;

        // Any inconsistency drops the whole upload, partial data never stays in memory
        if (ev.Index != upload.NextIndex ||
            length == 0 ||
            length > PlayerMusicLimits.ChunkBytes ||
            upload.Received + length > upload.Buffer.Length)
        {
            _pending.Remove(session.UserId);
            return;
        }

        Array.Copy(ev.Data, 0, upload.Buffer, upload.Received, length);
        upload.Received += length;
        upload.NextIndex++;
        upload.LastActivity = _timing.CurTime;

        if (upload.Received < upload.Buffer.Length)
            return;

        _pending.Remove(session.UserId);
        FinishUpload(session, upload);
    }

    private void FinishUpload(ICommonSession session, PendingUpload upload)
    {
        var boomboxUid = upload.Boombox;
        if (!TryComp(boomboxUid, out PlayerMusicBoomboxComponent? boomboxComp))
            return;

        var boombox = new Entity<PlayerMusicBoomboxComponent>(boomboxUid, boomboxComp);

        // The world could have changed while the chunks were on their way
        if (!CanRecord(session, boomboxUid, out var user, out var cassette))
            return;

        var data = upload.Buffer;
        var name = upload.Name;

        // Whole file is checked here: structure, end of stream, sample rate, channels and length
        if (!PlayerMusicOgg.TryAnalyze(data, out var info))
        {
            Deny(boomboxUid, user, "maid-music-not-ogg");
            return;
        }

        var maxSeconds = _cfg.GetCVar(PlayerMusicCVars.MaxSongSeconds);
        if (info.Seconds > maxSeconds)
        {
            Deny(boomboxUid, user, "maid-music-too-long", ("max", maxSeconds));
            return;
        }

        // Memory used after decoding does not depend on the file size, only on length, rate and channels
        if (info.DecodedBytes > _cfg.GetCVar(PlayerMusicCVars.MaxDecodedBytes))
        {
            Deny(boomboxUid, user, "maid-music-too-heavy");
            return;
        }

        var userId = session.UserId;
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

            _songs[id] = new StoredSong(id, name, data, userId, TimeSpan.FromSeconds(info.Seconds));
            _totalBytes += data.Length;
            owned.Add(id);
        }

        _lastUpload[userId] = _timing.CurTime;

        cassette.Comp.Tracks.Add(new PlayerMusicTrack(id, name));
        Dirty(cassette);
        UpdateCassetteInfo(boombox);

        var seconds = (int) info.Seconds;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} recorded song '{name}' ({data.Length} bytes, {seconds} s, id {id}) onto a cassette in {ToPrettyString(boomboxUid)}");

        var message = info.Channels == 2 ? "maid-music-recorded-stereo" : "maid-music-recorded";
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

        // The song is already on its way to this player
        if (_transfers.ContainsKey(session.UserId))
            return;

        // Dropped requests are fine: the client asks again after a few seconds
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

        _transfers[session.UserId] = new Transfer(session, song);
        RaiseNetworkEvent(new PlayerMusicFileStartEvent(song.Id, song.Data.Length), Filter.SinglePlayer(session), false);
    }

    private void OnCancelFile(PlayerMusicCancelFileEvent ev, EntitySessionEventArgs args)
    {
        // The player walked away, the rest of the song would only waste traffic
        if (_transfers.TryGetValue(args.SenderSession.UserId, out var transfer) && transfer.Song.Id == ev.SongId)
            _transfers.Remove(args.SenderSession.UserId);
    }

    /// <summary>
    /// Songs go out in small pieces, a few per tick, so one download never floods the connection.
    /// </summary>
    private void SendChunks()
    {
        if (_transfers.Count == 0)
            return;

        List<NetUserId>? done = null;

        foreach (var (user, transfer) in _transfers)
        {
            if (transfer.Session.Status != SessionStatus.InGame)
            {
                (done ??= new()).Add(user);
                continue;
            }

            var data = transfer.Song.Data;

            for (var i = 0; i < ChunksPerTick && transfer.Offset < data.Length; i++)
            {
                var length = Math.Min(PlayerMusicLimits.ChunkBytes, data.Length - transfer.Offset);
                var chunk = new byte[length];
                Array.Copy(data, transfer.Offset, chunk, 0, length);

                // recordReplay: false, so replays don't get filled with audio files
                RaiseNetworkEvent(
                    new PlayerMusicFileChunkEvent(transfer.Song.Id, transfer.Index++, chunk),
                    Filter.SinglePlayer(transfer.Session),
                    false);

                transfer.Offset += length;
            }

            if (transfer.Offset >= data.Length)
                (done ??= new()).Add(user);
        }

        if (done == null)
            return;

        foreach (var user in done)
        {
            _transfers.Remove(user);
        }
    }

    #endregion
}
