using System.Security.Cryptography;
using System.Text;
using Content.Shared._Maid.PlayerMusic;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.Player;
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
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private const int MaxNameLength = 40;

    /// <summary>
    /// Song ids are 32 hex chars of SHA-256; anything longer from a client is garbage.
    /// </summary>
    private const int MaxSongIdLength = 64;

    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// An upload that never finishes must not hold its buffer forever,
    /// no matter how slowly the chunks keep coming.
    /// </summary>
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Throttle for upload start attempts, so spam cannot churn multi-megabyte buffers.
    /// </summary>
    private static readonly TimeSpan UploadStartThrottle = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Throttle for the play/stop/repeat messages, so they cannot be toggled every tick.
    /// </summary>
    private static readonly TimeSpan UiActionThrottle = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Total chunks sent to all players per tick. Without a shared budget one song playing
    /// in a crowded room would saturate the connection with per-player transfers.
    /// </summary>
    private const int MaxChunksPerTick = 12;

    /// <summary>
    /// Max players downloading a song at the same time; extra requests are dropped and retried by the client.
    /// </summary>
    private const int MaxTransfers = 24;

    /// <summary>
    /// Total bytes of half-finished uploads kept in memory. Whole songs are already capped by max_total_bytes,
    /// but an upload is a big buffer before any validation happens.
    /// </summary>
    private const int MaxPendingBytes = 32_000_000;

    private sealed record StoredSong(string Id, string Name, byte[] Data, NetUserId Owner, TimeSpan Duration);

    private sealed class PendingUpload(EntityUid boombox, EntityUid cassette, string name, int total, TimeSpan now)
    {
        public readonly EntityUid Boombox = boombox;
        public readonly EntityUid Cassette = cassette;
        public readonly string Name = name;
        public readonly byte[] Buffer = new byte[total];
        public int Received;
        public int NextIndex;
        public readonly TimeSpan Created = now;
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
    private readonly Dictionary<string, int> _songRefs = new();
    private readonly Dictionary<NetUserId, HashSet<string>> _owned = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastUpload = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastUploadStart = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastRequest = new();
    private readonly Dictionary<NetUserId, PendingUpload> _pending = new();
    private readonly Dictionary<NetUserId, Transfer> _transfers = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastUiAction = new();
    private int _sendCursor;
    private TimeSpan _nextCheck;
    private long _totalBytes;
    private long _pendingBytes;

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
        SubscribeLocalEvent<PlayerMusicCassetteComponent, ComponentShutdown>(OnCassetteShutdown);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _songs.Clear();
        _songRefs.Clear();
        _owned.Clear();
        _lastUpload.Clear();
        _lastUploadStart.Clear();
        _lastRequest.Clear();
        _lastUiAction.Clear();
        _pending.Clear();
        _transfers.Clear();
        _totalBytes = 0;
        _pendingBytes = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        SendChunks();

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        // Interrupted uploads must not hold memory forever, no matter how slowly the chunks trickle
        List<NetUserId>? stale = null;
        foreach (var (user, upload) in _pending)
        {
            var dead = now - upload.LastActivity > UploadTimeout ||
                       now - upload.Created > UploadLifetime ||
                       !_player.TryGetSessionById(user, out var session) ||
                       session.Status != SessionStatus.InGame;

            if (dead)
                (stale ??= new()).Add(user);
        }

        if (stale != null)
        {
            foreach (var user in stale)
            {
                DropPending(user);
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

        if (IsThrottled(ent))
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

        var now = _timing.CurTime;
        _lastUiAction[ent.Owner] = now;

        ent.Comp.SongId = track.Id;
        ent.Comp.SongName = track.Name;
        ent.Comp.StartedAt = now;
        Dirty(ent);
        _appearance.SetData(ent, PlayerMusicVisuals.Playing, true);
    }

    private void OnStop(Entity<PlayerMusicBoomboxComponent> ent, ref PlayerMusicStopMessage args)
    {
        if (ent.Comp.StartedAt == null)
            return;

        // Stop always ends active playback: a throttled stop would leave the track playing
        // even though the button is enabled. Repeated stops no-op on the check above.
        _lastUiAction[ent.Owner] = _timing.CurTime;
        StopPlaying(ent);
    }

    private void OnRepeat(Entity<PlayerMusicBoomboxComponent> ent, ref PlayerMusicRepeatMessage args)
    {
        if (ent.Comp.Repeat == args.Repeat || IsThrottled(ent))
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

        _lastUiAction[ent.Owner] = _timing.CurTime;
        ent.Comp.Repeat = args.Repeat;
        Dirty(ent);
    }

    private bool IsThrottled(EntityUid ent)
    {
        return _lastUiAction.TryGetValue(ent, out var last) && _timing.CurTime - last < UiActionThrottle;
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

    private void OnCassetteShutdown(Entity<PlayerMusicCassetteComponent> ent, ref ComponentShutdown args)
    {
        // The cassette is gone, its songs can free up storage for new recordings
        foreach (var track in ent.Comp.Tracks)
            ReleaseSong(track.Id);
    }

    /// <summary>
    /// Songs are content-addressed and can sit on several cassettes; the storage frees up
    /// when the last cassette carrying the song is gone.
    /// </summary>
    private void ReleaseSong(string id)
    {
        if (!_songRefs.TryGetValue(id, out var refs))
            return;

        if (refs > 1)
        {
            _songRefs[id] = refs - 1;
            return;
        }

        _songRefs.Remove(id);

        if (_songs.Remove(id, out var song))
        {
            _totalBytes -= song.Data.Length;

            // The round quota stays charged: the player did record this song, even if every
            // cassette carrying it is gone now. _owned clears on round restart.
        }
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

        // Recording goes through the device's window; without this a raw-event client
        // could record while critted, as a mouse, or without ever opening the UI
        if (!_ui.IsUiOpen(boomboxUid, PlayerMusicUiKey.Key, user))
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
        var userId = session.UserId;

        // Any new start voids the client's previous upload: the client has already replaced it
        // with new chunks, so a stale pending must not survive to receive them
        DropPending(userId);

        // The throttle comes next: repeated starts must not churn multi-megabyte buffers
        var now = _timing.CurTime;
        if (_lastUploadStart.TryGetValue(userId, out var lastStart) && now - lastStart < UploadStartThrottle)
            return;
        _lastUploadStart[userId] = now;

        var boomboxUid = GetEntity(ev.Boombox);
        if (!CanRecord(session, boomboxUid, out var user, out var startCassette))
            return;

        // The client hard ceilings apply to admission too: a song above them would be stored
        // and then silently ignored by every client on download
        var maxBytes = Math.Min(_cfg.GetCVar(PlayerMusicCVars.MaxSongBytes), PlayerMusicLimits.HardMaxSongBytes);
        if (ev.TotalBytes <= 0 || ev.TotalBytes > maxBytes)
        {
            Deny(boomboxUid, user, "maid-music-too-big", ("max", FormatMb(maxBytes)));
            return;
        }

        // Half-finished uploads of all players together are capped too
        if (_pendingBytes + ev.TotalBytes > MaxPendingBytes)
        {
            Deny(boomboxUid, user, "maid-music-server-busy");
            return;
        }

        var name = SanitizeName(ev.Name);
        if (name.Length == 0)
        {
            Deny(boomboxUid, user, "maid-music-bad-name");
            return;
        }

        _pending[userId] = new PendingUpload(boomboxUid, startCassette, name, ev.TotalBytes, now);
        _pendingBytes += ev.TotalBytes;
    }

    private void OnUploadChunk(PlayerMusicUploadChunkEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_pending.TryGetValue(session.UserId, out var upload))
            return;

        if (ev.Data is not { } data)
        {
            DropPending(session.UserId);
            return;
        }

        var length = data.Length;

        // Any inconsistency drops the whole upload, partial data never stays in memory
        if (ev.Index != upload.NextIndex ||
            length == 0 ||
            length > PlayerMusicLimits.ChunkBytes ||
            upload.Received + length > upload.Buffer.Length)
        {
            DropPending(session.UserId);
            return;
        }

        Array.Copy(data, 0, upload.Buffer, upload.Received, length);
        upload.Received += length;
        upload.NextIndex++;
        upload.LastActivity = _timing.CurTime;

        if (upload.Received < upload.Buffer.Length)
            return;

        DropPending(session.UserId);
        FinishUpload(session, upload);
    }

    private void DropPending(NetUserId user)
    {
        if (_pending.Remove(user, out var upload))
            _pendingBytes -= upload.Buffer.Length;
    }

    private void FinishUpload(ICommonSession session, PendingUpload upload)
    {
        var boomboxUid = upload.Boombox;
        if (!TryComp(boomboxUid, out PlayerMusicBoomboxComponent? boomboxComp))
        {
            // The device was deleted while the chunks were on their way: tell the player instead of staying silent
            if (session.AttachedEntity is { } lost)
                _popup.PopupEntity(Loc.GetString("maid-music-recorder-lost"), lost, lost, PopupType.SmallCaution);

            return;
        }

        var boombox = new Entity<PlayerMusicBoomboxComponent>(boomboxUid, boomboxComp);

        // The world could have changed while the chunks were on their way
        if (!CanRecord(session, boomboxUid, out var user, out var cassette))
            return;

        // The cassette must still be the one the recording started on, otherwise the track
        // would land on whatever replaced it while the chunks were on their way
        if (cassette.Owner != upload.Cassette)
        {
            Deny(boomboxUid, user, "maid-music-cassette-changed");
            return;
        }

        var data = upload.Buffer;
        var name = upload.Name;

        // Whole file is checked here: structure, end of stream, sample rate, channels and length
        if (!PlayerMusicOgg.TryAnalyze(data, out var info))
        {
            Deny(boomboxUid, user, "maid-music-not-ogg");
            return;
        }

        // The engine only positions mono sources: stereo files would play without direction
        if (info.Channels != 1)
        {
            Deny(boomboxUid, user, "maid-music-stereo");
            return;
        }

        // The client hard ceilings apply here too, same as the byte cap in OnUploadStart
        var maxSeconds = Math.Min(_cfg.GetCVar(PlayerMusicCVars.MaxSongSeconds), PlayerMusicLimits.HardMaxSongSeconds);
        if (info.Seconds > maxSeconds)
        {
            Deny(boomboxUid, user, "maid-music-too-long", ("max", maxSeconds));
            return;
        }

        // Memory used after decoding does not depend on the file size, only on length, rate and channels
        var maxDecoded = Math.Min((long) _cfg.GetCVar(PlayerMusicCVars.MaxDecodedBytes), PlayerMusicLimits.HardMaxDecodedBytes);
        if (info.DecodedBytes > maxDecoded)
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
        _songRefs[id] = _songRefs.TryGetValue(id, out var refs) ? refs + 1 : 1;
        Dirty(cassette);
        UpdateCassetteInfo(boombox);

        var seconds = (int) info.Seconds;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} recorded song '{name}' ({data.Length} bytes, {seconds} s, id {id}) onto a cassette in {ToPrettyString(boomboxUid)}");

        _popup.PopupEntity(Loc.GetString("maid-music-recorded", ("name", name)), boomboxUid, user);
    }

    private void Deny(EntityUid boombox, EntityUid user, string locKey, params (string, object)[] args)
    {
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} was denied a music recording at {ToPrettyString(boombox)}: {locKey}");
        _popup.PopupEntity(Loc.GetString(locKey, args), boombox, user, PopupType.SmallCaution);
    }

    private static string FormatMb(int bytes)
    {
        return (bytes / 1_000_000f).ToString("0.##");
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        // Cut overly long input early, the real cap is applied below
        if (name.Length > MaxNameLength * 8)
            name = name[..(MaxNameLength * 8)];

        var trimmed = name.Trim();
        var sb = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (char.IsControl(c))
                continue;

            // Surrogate pairs (emoji) must travel together: an unpaired half renders as garbage.
            // Isolated halves are skipped entirely.
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= trimmed.Length || !char.IsLowSurrogate(trimmed[i + 1]))
                    continue;

                if (sb.Length + 2 > MaxNameLength)
                    break;

                sb.Append(c);
                sb.Append(trimmed[i + 1]);
                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
                continue;

            if (sb.Length + 1 > MaxNameLength)
                break;

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    #endregion

    #region Download on demand

    private void OnRequestFile(PlayerMusicRequestFileEvent ev, EntitySessionEventArgs args)
    {
        if (!_cfg.GetCVar(PlayerMusicCVars.Enabled))
            return;

        var session = args.SenderSession;
        if (session.AttachedEntity is not { } user)
            return;

        // The song is already on its way to this player
        if (_transfers.ContainsKey(session.UserId))
            return;

        // Too many players downloading at once: drop quietly, the client asks again
        if (_transfers.Count >= MaxTransfers)
            return;

        // Dropped requests are fine: the client asks again after a few seconds
        var now = _timing.CurTime;
        if (_lastRequest.TryGetValue(session.UserId, out var last) && now - last < TimeSpan.FromSeconds(1))
            return;

        _lastRequest[session.UserId] = now;

        if (ev.SongId is not { } songId || songId.Length > MaxSongIdLength)
            return;

        var boombox = GetEntity(ev.Boombox);
        if (!TryComp(boombox, out PlayerMusicBoomboxComponent? comp) ||
            comp.StartedAt == null ||
            comp.SongId != songId)
        {
            return;
        }

        if (!_songs.TryGetValue(songId, out var song))
            return;

        var range = _cfg.GetCVar(PlayerMusicCVars.ListenRange);
        if (!_xform.InRange(Transform(user).Coordinates, Transform(boombox).Coordinates, range))
            return;

        _transfers[session.UserId] = new Transfer(session, song);
        RaiseNetworkEvent(new PlayerMusicFileStartEvent(song.Id, song.Data.Length), Filter.SinglePlayer(session), false);
    }

    private void OnCancelFile(PlayerMusicCancelFileEvent ev, EntitySessionEventArgs args)
    {
        if (ev.SongId is not { } songId || songId.Length > MaxSongIdLength)
            return;

        // The player walked away, the rest of the song would only waste traffic
        if (_transfers.TryGetValue(args.SenderSession.UserId, out var transfer) && transfer.Song.Id == songId)
            _transfers.Remove(args.SenderSession.UserId);
    }

    /// <summary>
    /// Songs go out in small pieces with a shared per-tick budget. The budget is spent one chunk
    /// at a time walking a ring of transfers that keeps rotating between ticks, so the cap is
    /// never exceeded and a crowd around a boombox shares it fairly.
    /// </summary>
    private void SendChunks()
    {
        if (_transfers.Count == 0)
            return;

        // The kill switch stops the ongoing downloads too
        if (!_cfg.GetCVar(PlayerMusicCVars.Enabled))
        {
            _transfers.Clear();
            return;
        }

        var range = _cfg.GetCVar(PlayerMusicCVars.ListenRange);
        List<NetUserId>? done = null;
        var order = new List<NetUserId>(_transfers.Keys);

        if (_sendCursor >= order.Count)
            _sendCursor = 0;

        for (var n = 0; n < MaxChunksPerTick; n++)
        {
            var user = order[(_sendCursor + n) % order.Count];
            var transfer = _transfers[user];

            // Finished, disconnected or unwanted: no chunk for it, the ring drops it below
            if (transfer.Session.Status != SessionStatus.InGame ||
                transfer.Offset >= transfer.Song.Data.Length ||
                !IsTransferWanted(transfer, range))
            {
                (done ??= new()).Add(user);
                continue;
            }

            var data = transfer.Song.Data;
            var length = Math.Min(PlayerMusicLimits.ChunkBytes, data.Length - transfer.Offset);
            var chunk = new byte[length];
            Array.Copy(data, transfer.Offset, chunk, 0, length);

            // recordReplay: false, so replays don't get filled with audio files
            RaiseNetworkEvent(
                new PlayerMusicFileChunkEvent(transfer.Song.Id, transfer.Index++, chunk),
                Filter.SinglePlayer(transfer.Session),
                false);

            transfer.Offset += length;

            if (transfer.Offset >= data.Length)
                (done ??= new()).Add(user);
        }

        // The rotation continues from this position on the next tick
        _sendCursor = (_sendCursor + MaxChunksPerTick) % order.Count;

        if (done == null)
            return;

        foreach (var user in done)
        {
            _transfers.Remove(user);
        }
    }

    /// <summary>
    /// The song must still play near the listener on some boombox: teleports, stopped songs and
    /// stopped devices cancel the transfer, but another device taking over the same song keeps
    /// it alive without the client having to re-request it.
    /// </summary>
    private bool IsTransferWanted(Transfer transfer, float range)
    {
        if (transfer.Session.AttachedEntity is not { } user)
            return false;

        var userCoords = Transform(user).Coordinates;

        var query = EntityQueryEnumerator<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.StartedAt == null || comp.SongId != transfer.Song.Id)
                continue;

            if (_xform.InRange(userCoords, Transform(uid).Coordinates, range))
                return true;
        }

        return false;
    }

    #endregion
}
