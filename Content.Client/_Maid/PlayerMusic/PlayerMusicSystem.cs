using Content.Shared.GameTicking;
using Content.Shared._Maid.PlayerMusic;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Maid.PlayerMusic;

/// <summary>
/// Plays boombox songs locally, but only while this client is within hearing distance of the boombox.
/// The audio file is requested from the server only when needed and downloaded in small chunks;
/// a few downloaded songs are kept in memory.
/// </summary>
public sealed class PlayerMusicSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IClientNetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    /// <summary>
    /// Every cached song is fully decoded in memory, so only a few are kept.
    /// </summary>
    private const int MaxCachedSongs = 3;

    /// <summary>
    /// Byte budget for the decoded song cache. One long song takes ~46 MB decoded,
    /// and songs that are currently playing are never evicted, so the count cap alone is not enough.
    /// Playing songs may temporarily exceed this budget; space is reclaimed as soon as they stop.
    /// </summary>
    private const long MaxCachedBytes = 128_000_000;

    /// <summary>
    /// Upload speed cap in bytes per second, same as the original implementation.
    /// </summary>
    private const int UploadBytesPerSecond = 1_000_000;

    /// <summary>
    /// Downloads of different songs at the same time are capped: a hostile server must not be able
    /// to make the client hold an unbounded number of buffers.
    /// </summary>
    private const int MaxIncomingDownloads = 3;

    /// <summary>
    /// A song that already started playing is dropped only after the player walks this far past the hearing distance.
    /// </summary>
    private const float RangeMargin = 3f;

    private static readonly TimeSpan RequestRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IncomingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UploadResultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan EndSlack = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// A song rejected by a configurable server limit is retried after a while: an admin may
    /// restore the limit, and the song must start for listeners who are still waiting.
    /// </summary>
    private static readonly TimeSpan LimitRetry = TimeSpan.FromSeconds(60);

    private static ulong _shareIdx;

    private readonly MemoryContentRoot _contentRoot = new();
    private ResPath _prefix;
    private ulong _fileIdx;
    private TimeSpan _nextCheck;
    private float _uploadBudget;
    private float _userGain = 1f;

    private readonly Dictionary<string, AudioResource> _cache = new();
    private readonly Dictionary<string, long> _cacheSizes = new();
    private readonly Queue<string> _cacheOrder = new();
    private long _cacheBytes;
    private readonly Dictionary<string, TimeSpan> _requested = new();
    private readonly Dictionary<string, IncomingFile> _incoming = new();
    private readonly HashSet<string> _failed = new();
    private readonly Dictionary<string, TimeSpan> _limitFailed = new();
    private readonly Dictionary<EntityUid, PlayingState> _playing = new();
    private byte[]? _uploadData;
    private NetEntity _uploadBoombox;
    private string _uploadName = string.Empty;
    private int _uploadOffset;
    private int _uploadIndex;
    private int _uploadTotal;
    private int _lastUploadPercent = -1;
    private bool _uploadStartPending;
    private bool _uploadAwaiting;
    private TimeSpan _uploadDeadline;

    private readonly record struct PlayingState(
        EntityUid AudioEntity,
        string SongId,
        TimeSpan StartedAt,
        bool Repeat);

    private sealed class IncomingFile(int total, TimeSpan now)
    {
        public readonly byte[] Buffer = new byte[total];
        public int Received;
        public int NextIndex;
        public TimeSpan Last = now;
    }

    public override void Initialize()
    {
        base.Initialize();

        _prefix = ResPath.Root / $"PlayerMusic{_shareIdx++}";
        _res.AddRoot(_prefix, _contentRoot);

        _cfg.OnValueChanged(PlayerMusicCVars.Volume, OnVolumeChanged, true);
        _net.RegisterNetMessage<PlayerMusicUploadStartNetMessage>();
        _net.RegisterNetMessage<PlayerMusicUploadChunkNetMessage>();

        SubscribeNetworkEvent<PlayerMusicFileStartEvent>(OnFileStart);
        SubscribeNetworkEvent<PlayerMusicFileChunkEvent>(OnFileChunk);
        SubscribeNetworkEvent<PlayerMusicRecordStatusEvent>(OnRecordStatus);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// The server forgets all songs on restart, so ids and failure marks from the last round mean nothing now.
    /// Decoded songs stay cached: their ids are content hashes, they are still valid.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _requested.Clear();
        _failed.Clear();
        _limitFailed.Clear();
        _incoming.Clear();
        ResetUpload();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _cfg.UnsubValueChanged(PlayerMusicCVars.Volume, OnVolumeChanged);

        foreach (var state in _playing.Values)
        {
            DeleteAudio(state.AudioEntity);
        }

        _playing.Clear();

        foreach (var resource in _cache.Values)
        {
            resource.AudioStream.Dispose();
        }

        _cache.Clear();
        _cacheSizes.Clear();
        _cacheBytes = 0;
        _cacheOrder.Clear();
        _requested.Clear();
        _failed.Clear();
        _limitFailed.Clear();
        _incoming.Clear();
        ResetUpload();
        _contentRoot.Clear();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Boombox volume from the player's audio settings, applied on top of the boombox's own volume.
    /// </summary>
    private float GetVolume(PlayerMusicBoomboxComponent comp)
    {
        var gain = Math.Max(_userGain * comp.VolumeScale, 0.0001f);
        return comp.Volume + SharedAudioSystem.GainToVolume(gain);
    }

    private void OnVolumeChanged(float value)
    {
        _userGain = Math.Clamp(value, 0f, 2f);

        foreach (var (uid, state) in _playing)
        {
            if (!Exists(state.AudioEntity) || !TryComp(uid, out PlayerMusicBoomboxComponent? comp))
                continue;

            _audio.SetVolume(state.AudioEntity, GetVolume(comp));
        }
    }

    /// <summary>
    /// Sends the song to the server in small pieces, a few per frame.
    /// </summary>
    public void RequestUpload(EntityUid boombox, string name, byte[] data)
    {
        // A new recording replaces anything that is still waiting to be sent
        ResetUpload();
        _uploadData = data;
        _uploadBoombox = GetNetEntity(boombox);
        _uploadName = name;
        _uploadTotal = data.Length;
        _lastUploadPercent = -1;
        _uploadStartPending = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        PumpUploadQueue(frameTime);

        var now = _timing.CurTime;
        CheckUploadTimeout(now);

        if (now >= _nextCheck)
        {
            _nextCheck = now + CheckInterval;
            Maintain(now);
        }
    }

    /// <summary>
    /// Sends queued upload messages at a capped speed: a few kilobytes per frame
    /// instead of pushing megabytes into the connection in one burst.
    /// </summary>
    private void PumpUploadQueue(float frameTime)
    {
        if (_uploadStartPending)
        {
            _uploadStartPending = false;
            _net.ClientSendMessage(new PlayerMusicUploadStartNetMessage
            {
                Boombox = _uploadBoombox,
                Name = _uploadName,
                TotalBytes = _uploadData?.Length ?? 0,
            });
        }

        if (_uploadData == null)
            return;

        // The budget is capped at one protocol chunk, so a frame hitch cannot turn into a large burst
        _uploadBudget = Math.Min(_uploadBudget + UploadBytesPerSecond * frameTime, PlayerMusicLimits.ChunkBytes);

        while (_uploadData != null)
        {
            var remaining = _uploadData.Length - _uploadOffset;
            if (remaining <= 0)
            {
                _uploadData = null;
                _uploadAwaiting = true;
                _uploadDeadline = _timing.CurTime + UploadResultTimeout;
                break;
            }

            var length = Math.Min(PlayerMusicLimits.ChunkBytes, remaining);
            if (length > _uploadBudget)
                break;

            var chunk = new byte[length];
            Array.Copy(_uploadData, _uploadOffset, chunk, 0, length);
            _uploadOffset += length;
            _uploadBudget -= length;
            _net.ClientSendMessage(new PlayerMusicUploadChunkNetMessage
            {
                Index = _uploadIndex++,
                Data = chunk,
            });
        }

        ReportUploadProgress();
    }

    private void ReportUploadProgress()
    {
        if (_uploadTotal <= 0)
            return;

        var percent = (int) Math.Clamp(_uploadOffset * 100L / _uploadTotal, 0, 100);
        if (percent == _lastUploadPercent)
            return;

        _lastUploadPercent = percent;

        if (!TryGetEntity(_uploadBoombox, out var uid))
            return;

        if (_ui.TryGetOpenUi<PlayerMusicBoundUserInterface>(uid.Value, PlayerMusicUiKey.Key, out var bui))
            bui.SetUploadProgress(percent);
    }

    private void ResetUpload()
    {
        _uploadData = null;
        _uploadStartPending = false;
        _uploadAwaiting = false;
        _uploadDeadline = default;
        _uploadOffset = 0;
        _uploadIndex = 0;
        _uploadTotal = 0;
        _lastUploadPercent = -1;
        _uploadBudget = 0f;
    }

    public void CancelUpload()
    {
        if (_uploadStartPending)
        {
            ResetUpload();
            return;
        }

        if (_uploadData == null)
            return;

        if (_net.IsConnected)
        {
            _net.ClientSendMessage(new PlayerMusicUploadChunkNetMessage
            {
                Index = _uploadIndex,
                Data = [],
            });
        }

        ResetUpload();
    }

    private void CheckUploadTimeout(TimeSpan now)
    {
        if (!_uploadAwaiting || now < _uploadDeadline)
            return;

        if (TryGetEntity(_uploadBoombox, out var uid) &&
            _ui.TryGetOpenUi<PlayerMusicBoundUserInterface>(uid.Value, PlayerMusicUiKey.Key, out var bui))
        {
            bui.SetStatus("maid-music-upload-failed", string.Empty, 0f);
        }

        ResetUpload();
    }

    /// <summary>
    /// Runs twice a second: forgets stalled downloads, and re-checks every playing boombox,
    /// which starts/stops playback as the player walks around and retries requests the server dropped.
    /// </summary>
    private void Maintain(TimeSpan now)
    {
        if (_incoming.Count > 0)
        {
            List<string>? stalled = null;
            foreach (var (songId, file) in _incoming)
            {
                if (now - file.Last > IncomingTimeout)
                    (stalled ??= new()).Add(songId);
            }

            if (stalled != null)
            {
                foreach (var songId in stalled)
                {
                    _incoming.Remove(songId);
                }
            }
        }

        CancelUnneededDownloads();

        var query = AllEntityQuery<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.StartedAt != null || _playing.ContainsKey(uid))
                Sync((uid, comp));
        }
    }

    /// <summary>
    /// If nobody who is still in hearing distance needs a song that is being downloaded,
    /// tell the server to stop sending it.
    /// </summary>
    private void CancelUnneededDownloads()
    {
        if (_incoming.Count == 0)
            return;

        List<string>? unneeded = null;
        foreach (var songId in _incoming.Keys)
        {
            var needed = false;
            var query = AllEntityQuery<PlayerMusicBoomboxComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.StartedAt != null && comp.SongId == songId && InHearingRange((uid, comp), RangeMargin))
                {
                    needed = true;
                    break;
                }
            }

            if (!needed)
                (unneeded ??= new()).Add(songId);
        }

        if (unneeded == null)
            return;

        foreach (var songId in unneeded)
        {
            _incoming.Remove(songId);
            RaiseNetworkEvent(new PlayerMusicCancelFileEvent(songId));
        }
    }

    private void OnState(Entity<PlayerMusicBoomboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Sync(ent);

        if (_playing.TryGetValue(ent.Owner, out var state) && Exists(state.AudioEntity))
            _audio.SetVolume(state.AudioEntity, GetVolume(ent.Comp));

        if (_ui.TryGetOpenUi<PlayerMusicBoundUserInterface>(ent.Owner, PlayerMusicUiKey.Key, out var bui))
            bui.Reload();
    }

    private void OnRecordStatus(PlayerMusicRecordStatusEvent ev)
    {
        ResetUpload();

        if (!TryGetEntity(ev.Boombox, out var uid))
            return;

        if (_ui.TryGetOpenUi<PlayerMusicBoundUserInterface>(uid.Value, PlayerMusicUiKey.Key, out var bui))
            bui.SetStatus(ev.LocKey, ev.Text, ev.Number);
    }

    private void OnShutdown(Entity<PlayerMusicBoomboxComponent> ent, ref ComponentShutdown args)
    {
        StopLocal(ent.Owner);
    }

    /// <summary>
    /// Is the local player close enough to hear this boombox?
    /// </summary>
    private bool InHearingRange(Entity<PlayerMusicBoomboxComponent> ent, float margin)
    {
        if (_player.LocalEntity is not { } local || !Exists(local))
            return false;

        if (Transform(ent.Owner).MapID != Transform(local).MapID)
            return false;

        var distance = (_xform.GetWorldPosition(ent.Owner) - _xform.GetWorldPosition(local)).Length();
        return distance <= ent.Comp.MaxDistance + margin;
    }

    private void Sync(Entity<PlayerMusicBoomboxComponent> ent)
    {
        var comp = ent.Comp;

        if (comp.StartedAt is not { } startedAt || comp.SongId is not { } songId)
        {
            StopLocal(ent.Owner);
            return;
        }

        var alreadyPlaying = _playing.ContainsKey(ent.Owner);

        // Out of hearing distance: nothing is requested, decoded or played
        if (!InHearingRange(ent, alreadyPlaying ? RangeMargin : 0f))
        {
            StopLocal(ent.Owner);
            return;
        }

        // These abandonments run before the restart below: a song that cannot play right now
        // must not keep its playback slot, and every such exit reclaims the decoded bytes
        if (_failed.Contains(songId))
        {
            StopLocal(ent.Owner);
            return;
        }

        // A song rejected by a server limit is retried after a while: the limit may have been restored
        if (_limitFailed.TryGetValue(songId, out var limitAt))
        {
            if (_timing.CurTime - limitAt < LimitRetry)
            {
                StopLocal(ent.Owner);
                return;
            }

            _limitFailed.Remove(songId);
        }

        if (!_cache.TryGetValue(songId, out var resource))
        {
            StopLocal(ent.Owner);
            RequestFile(ent.Owner, songId);
            return;
        }

        if (_playing.TryGetValue(ent.Owner, out var current))
        {
            if (current.SongId == songId && current.StartedAt == startedAt && current.Repeat == comp.Repeat)
                return;

            // New run, or "repeat" was switched. The loop flag of a sound cannot be changed while it plays,
            // so it is restarted at the same position (the server keeps StartedAt consistent for this).
            // The eviction is deferred: for a moment the restarted song is not playing, and a full
            // cache must not evict it right before it is started again.
            StopLocal(ent.Owner, evict: false);
        }

        var stream = resource.AudioStream;

        var elapsed = _timing.CurTime - startedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (comp.Repeat)
        {
            // Join the loop at the right place
            if (stream.Length > TimeSpan.Zero)
                elapsed = TimeSpan.FromTicks(elapsed.Ticks % stream.Length.Ticks);
        }
        else if (elapsed >= stream.Length - EndSlack)
        {
            // The song is over. Starting it now would make the engine play its last few milliseconds.
            // The deferred eviction from the restart above must still run: the abandoned playback
            // freed its slot, and the finished song's bytes can be reclaimed.
            EvictOldSongs();
            return;
        }

        var audioParams = AudioParams.Default
            .WithVolume(GetVolume(comp))
            .WithMaxDistance(comp.MaxDistance)
            .WithLoop(comp.Repeat)
            .WithPlayOffset((float) elapsed.TotalSeconds);

        (EntityUid Entity, AudioComponent Component)? result;
        try
        {
            result = _audio.PlayEntity(stream, ent.Owner, null, audioParams);
        }
        catch (Exception e)
        {
            Log.Error($"PlayerMusic: failed to start playback: {e}");
            _failed.Add(songId);

            // The cached bytes of a permanently failed song are dead weight: reclaim them
            EvictOldSongs();
            return;
        }

        // Audio could not start (e.g. no output device): the song stays cached and is retried,
        // an eviction here would make a device-less client re-download it forever
        if (result is not { } played)
            return;

        _playing[ent.Owner] = new PlayingState(played.Entity, songId, startedAt, comp.Repeat);

        // The playback slot was freed above without evicting: reclaim the space now that
        // the restarted song is playing again and protected from eviction
        EvictOldSongs();
    }

    private void StopLocal(EntityUid boombox, bool evict = true)
    {
        if (!_playing.Remove(boombox, out var state))
            return;

        DeleteAudio(state.AudioEntity);

        if (evict)
        {
            // The stopped song no longer needs protection from eviction: free its decoded
            // bytes right away instead of waiting for the next download to trigger it
            EvictOldSongs();
        }
    }

    private void DeleteAudio(EntityUid audio)
    {
        if (!Exists(audio))
            return;

        // Stop deletes the audio entity
        _audio.Stop(audio);
    }

    private void RequestFile(EntityUid boombox, string songId)
    {
        // Already downloading it
        if (_incoming.ContainsKey(songId))
            return;

        var now = _timing.CurTime;
        if (_requested.TryGetValue(songId, out var last) && now - last < RequestRetry)
            return;

        _requested[songId] = now;
        RaiseNetworkEvent(new PlayerMusicRequestFileEvent(GetNetEntity(boombox), songId));
    }

    private void OnFileStart(PlayerMusicFileStartEvent ev)
    {
        // Only accept what we asked for
        if (ev.SongId == null || !_requested.ContainsKey(ev.SongId) || _cache.ContainsKey(ev.SongId))
            return;

        // Already downloading it, and a few downloads at most
        if (_incoming.ContainsKey(ev.SongId) || _incoming.Count >= MaxIncomingDownloads)
            return;

        if (ev.TotalBytes <= 0 || ev.TotalBytes > PlayerMusicLimits.HardMaxSongBytes)
        {
            Fail(ev.SongId, "unexpected file size");
            return;
        }

        _incoming[ev.SongId] = new IncomingFile(ev.TotalBytes, _timing.CurTime);
    }

    private void OnFileChunk(PlayerMusicFileChunkEvent ev)
    {
        if (ev.SongId == null || !_incoming.TryGetValue(ev.SongId, out var file))
            return;

        if (ev.Data is not { } data)
        {
            _incoming.Remove(ev.SongId);
            return;
        }

        var length = data.Length;

        // Any inconsistency drops the download, it is requested again by the retry
        if (ev.Index != file.NextIndex ||
            length == 0 ||
            length > PlayerMusicLimits.ChunkBytes ||
            file.Received + length > file.Buffer.Length)
        {
            _incoming.Remove(ev.SongId);
            return;
        }

        Array.Copy(data, 0, file.Buffer, file.Received, length);
        file.Received += length;
        file.NextIndex++;
        file.Last = _timing.CurTime;

        if (file.Received < file.Buffer.Length)
            return;

        _incoming.Remove(ev.SongId);
        OnFileComplete(ev.SongId, file.Buffer);
    }

    private void OnFileComplete(string songId, byte[] data)
    {
        // NOTE: the file hash cannot be verified against the song id here: System.Security.
        // Cryptography is not allowed in the client sandbox. The server binds the id to the
        // data at record time, and the structural checks below still guard the decoder.

        // Never give unchecked data to the decoder: it decodes the whole file into memory.
        // The hard limits bound what a hostile server can make the client allocate.
        var maxDecoded = Math.Min(PlayerMusicLimits.HardMaxDecodedBytes, (long) _cfg.GetCVar(PlayerMusicCVars.MaxDecodedBytes));

        if (!PlayerMusicOgg.TryAnalyze(data, out var info))
        {
            Fail(songId, "file is damaged");
            return;
        }

        if (info.Seconds > PlayerMusicLimits.HardMaxSongSeconds ||
            info.DecodedBytes > PlayerMusicLimits.HardMaxDecodedBytes)
        {
            Fail(songId, "file exceeds the hard playback limits");
            return;
        }

        if (info.DecodedBytes > maxDecoded)
        {
            // A configurable limit was lowered below this song: retryable, not a broken file
            LimitFail(songId, "file exceeds the current server limits");
            return;
        }

        var filePath = new ResPath($"{_fileIdx++}.ogg");
        _contentRoot.AddOrUpdateFile(filePath, data);

        AudioResource? resource = null;
        try
        {
            resource = new AudioResource();
            resource.Load(IoCManager.Instance!, _prefix / filePath);
        }
        catch (Exception e)
        {
            Log.Error($"PlayerMusic: failed to load song {songId}: {e}");
            resource = null;
        }
        finally
        {
            // The stream is loaded (or loading failed), the raw bytes must not stay in memory either way
            _contentRoot.RemoveFile(filePath);
        }

        if (resource == null)
        {
            Fail(songId, "decoding failed");
            return;
        }

        _cache[songId] = resource;
        _cacheSizes[songId] = info.DecodedBytes;
        _cacheBytes += info.DecodedBytes;
        _cacheOrder.Enqueue(songId);
        _requested.Remove(songId);
        _limitFailed.Remove(songId);

        // Start playback on every boombox that was waiting for this song. This runs before the
        // eviction below: a playing song is protected from it, so a full cache cannot evict the
        // freshly decoded song right before it starts and make the client download it again.
        var query = AllEntityQuery<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SongId == songId && comp.StartedAt != null)
                Sync((uid, comp));
        }

        EvictOldSongs();
    }

    /// <summary>
    /// A broken song is not requested again, so the boombox stays quiet instead of re-downloading it forever.
    /// </summary>
    private void Fail(string songId, string reason)
    {
        Log.Warning($"PlayerMusic: song {songId} rejected: {reason}");
        _failed.Add(songId);
        _limitFailed.Remove(songId);
        _incoming.Remove(songId);
        _requested.Remove(songId);
    }

    /// <summary>
    /// A song rejected by a configurable limit is not broken: it is retried after a while,
    /// and starts playing once the limit permits it again.
    /// </summary>
    private void LimitFail(string songId, string reason)
    {
        Log.Warning($"PlayerMusic: song {songId} rejected by a server limit: {reason}");
        _limitFailed[songId] = _timing.CurTime;
        _incoming.Remove(songId);
        _requested.Remove(songId);
    }

    private void EvictOldSongs()
    {
        var attempts = _cacheOrder.Count;
        while (attempts-- > 0 &&
               _cacheOrder.Count > 0 &&
               (_cache.Count > MaxCachedSongs || _cacheBytes > MaxCachedBytes))
        {
            var old = _cacheOrder.Dequeue();

            if (IsPlaying(old))
            {
                _cacheOrder.Enqueue(old);
                continue;
            }

            // Disposing frees the OpenAL buffer, dropping the reference alone would not
            if (_cache.Remove(old, out var resource))
            {
                _cacheBytes -= _cacheSizes.GetValueOrDefault(old);
                _cacheSizes.Remove(old);
                resource.AudioStream.Dispose();
            }
        }
    }

    private bool IsPlaying(string songId)
    {
        foreach (var state in _playing.Values)
        {
            if (state.SongId == songId)
                return true;
        }

        return false;
    }
}
