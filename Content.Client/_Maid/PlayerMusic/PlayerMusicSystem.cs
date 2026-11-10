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
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    /// <summary>
    /// Every cached song is fully decoded in memory, so only a few are kept.
    /// </summary>
    private const int MaxCachedSongs = 3;

    private const int ChunksPerFrame = 4;

    /// <summary>
    /// A song that already started playing is dropped only after the player walks this far past the hearing distance.
    /// </summary>
    private const float RangeMargin = 3f;

    private static readonly TimeSpan RequestRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IncomingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan EndSlack = TimeSpan.FromMilliseconds(50);
    private static ulong _shareIdx;

    private readonly MemoryContentRoot _contentRoot = new();
    private ResPath _prefix;
    private ulong _fileIdx;
    private TimeSpan _nextCheck;
    private float _userGain = 1f;

    private readonly Dictionary<string, AudioResource> _cache = new();
    private readonly Queue<string> _cacheOrder = new();
    private readonly Dictionary<string, TimeSpan> _requested = new();
    private readonly Dictionary<string, IncomingFile> _incoming = new();
    private readonly HashSet<string> _failed = new();
    private readonly Dictionary<EntityUid, PlayingState> _playing = new();
    private readonly Queue<EntityEventArgs> _outgoing = new();

    private readonly record struct PlayingState(
        EntityUid AudioEntity,
        string SongId,
        TimeSpan StartedAt,
        bool Repeat,
        bool Stereo);

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

        SubscribeNetworkEvent<PlayerMusicFileStartEvent>(OnFileStart);
        SubscribeNetworkEvent<PlayerMusicFileChunkEvent>(OnFileChunk);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, ComponentShutdown>(OnShutdown);
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
        _cacheOrder.Clear();
        _incoming.Clear();
        _outgoing.Clear();
        _contentRoot.Clear();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Boombox volume from the player's audio settings, applied on top of the boombox's own volume.
    /// </summary>
    private float GetVolume(PlayerMusicBoomboxComponent comp, float distanceGain = 1f)
    {
        return comp.Volume + SharedAudioSystem.GainToVolume(Math.Max(distanceGain * _userGain, 0.0001f));
    }

    private void OnVolumeChanged(float value)
    {
        _userGain = Math.Max(value, 0f);

        // Mono sounds are changed here, stereo ones are updated every frame together with the distance fade
        foreach (var (uid, state) in _playing)
        {
            if (state.Stereo || !Exists(state.AudioEntity) || !TryComp(uid, out PlayerMusicBoomboxComponent? comp))
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
        _outgoing.Clear();
        _outgoing.Enqueue(new PlayerMusicUploadStartEvent(GetNetEntity(boombox), name, data.Length));

        var index = 0;
        for (var offset = 0; offset < data.Length; offset += PlayerMusicLimits.ChunkBytes)
        {
            var length = Math.Min(PlayerMusicLimits.ChunkBytes, data.Length - offset);
            var chunk = new byte[length];
            Array.Copy(data, offset, chunk, 0, length);
            _outgoing.Enqueue(new PlayerMusicUploadChunkEvent(index++, chunk));
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        for (var i = 0; i < ChunksPerFrame && _outgoing.Count > 0; i++)
        {
            RaiseNetworkEvent(_outgoing.Dequeue());
        }

        var now = _timing.CurTime;
        if (now >= _nextCheck)
        {
            _nextCheck = now + CheckInterval;
            Maintain(now);
        }

        UpdateStereoFade();
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

    /// <summary>
    /// Stereo audio cannot be positional in OpenAL (the engine only positions mono sources),
    /// so for stereo songs we fade the volume with distance ourselves.
    /// </summary>
    private void UpdateStereoFade()
    {
        if (_playing.Count == 0)
            return;

        var local = _player.LocalEntity;

        foreach (var (uid, state) in _playing)
        {
            if (!state.Stereo || !Exists(state.AudioEntity))
                continue;

            if (!TryComp(uid, out PlayerMusicBoomboxComponent? comp))
                continue;

            var gain = 1f;
            if (local != null && Exists(local.Value))
            {
                var distance = (_xform.GetWorldPosition(uid) - _xform.GetWorldPosition(local.Value)).Length();
                gain = Math.Clamp(1f - distance / comp.MaxDistance, 0f, 1f);
                gain *= gain;
            }

            _audio.SetVolume(state.AudioEntity, GetVolume(comp, gain));
        }
    }

    private void OnState(Entity<PlayerMusicBoomboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Sync(ent);

        if (_ui.TryGetOpenUi<PlayerMusicBoundUserInterface>(ent.Owner, PlayerMusicUiKey.Key, out var bui))
            bui.Reload();
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

        if (_playing.TryGetValue(ent.Owner, out var current))
        {
            if (current.SongId == songId && current.StartedAt == startedAt && current.Repeat == comp.Repeat)
                return;

            // New run, or "repeat" was switched. The loop flag of a sound cannot be changed while it plays,
            // so it is restarted at the same position (the server keeps StartedAt consistent for this).
            StopLocal(ent.Owner);
        }

        if (_failed.Contains(songId))
            return;

        if (!_cache.TryGetValue(songId, out var resource))
        {
            RequestFile(ent.Owner, songId);
            return;
        }

        var stream = resource.AudioStream;
        var stereo = stream.ChannelCount > 1;

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
            result = stereo
                ? _audio.PlayGlobal(stream, null, audioParams)
                : _audio.PlayEntity(stream, ent.Owner, null, audioParams);
        }
        catch (Exception e)
        {
            Log.Error($"PlayerMusic: failed to start playback: {e}");
            _failed.Add(songId);
            return;
        }

        if (result is not { } played)
            return;

        _playing[ent.Owner] = new PlayingState(played.Entity, songId, startedAt, comp.Repeat, stereo);
    }

    private void StopLocal(EntityUid boombox)
    {
        if (!_playing.Remove(boombox, out var state))
            return;

        DeleteAudio(state.AudioEntity);
    }

    private void DeleteAudio(EntityUid audio)
    {
        if (!Exists(audio))
            return;

        _audio.Stop(audio);

        if (Exists(audio))
            QueueDel(audio);
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
        if (!_requested.ContainsKey(ev.SongId) || _cache.ContainsKey(ev.SongId))
            return;

        if (ev.TotalBytes <= 0 || ev.TotalBytes > _cfg.GetCVar(PlayerMusicCVars.MaxSongBytes))
        {
            Fail(ev.SongId, "unexpected file size");
            return;
        }

        _incoming[ev.SongId] = new IncomingFile(ev.TotalBytes, _timing.CurTime);
    }

    private void OnFileChunk(PlayerMusicFileChunkEvent ev)
    {
        if (!_incoming.TryGetValue(ev.SongId, out var file))
            return;

        var length = ev.Data.Length;

        // Any inconsistency drops the download, it is requested again by the retry
        if (ev.Index != file.NextIndex ||
            length == 0 ||
            length > PlayerMusicLimits.ChunkBytes ||
            file.Received + length > file.Buffer.Length)
        {
            _incoming.Remove(ev.SongId);
            return;
        }

        Array.Copy(ev.Data, 0, file.Buffer, file.Received, length);
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
        // Never give unchecked data to the decoder: it decodes the whole file into memory
        if (!PlayerMusicOgg.TryAnalyze(data, out var info) ||
            info.Seconds > _cfg.GetCVar(PlayerMusicCVars.MaxSongSeconds) ||
            info.DecodedBytes > _cfg.GetCVar(PlayerMusicCVars.MaxDecodedBytes))
        {
            Fail(songId, "file is damaged or too big after decoding");
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
        _cacheOrder.Enqueue(songId);
        _requested.Remove(songId);
        EvictOldSongs();

        // Start playback on every boombox that was waiting for this song
        var query = AllEntityQuery<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SongId == songId && comp.StartedAt != null)
                Sync((uid, comp));
        }
    }

    /// <summary>
    /// A broken song is not requested again, so the boombox stays quiet instead of re-downloading it forever.
    /// </summary>
    private void Fail(string songId, string reason)
    {
        Log.Warning($"PlayerMusic: song {songId} rejected: {reason}");
        _failed.Add(songId);
        _incoming.Remove(songId);
        _requested.Remove(songId);
    }

    private void EvictOldSongs()
    {
        var attempts = _cacheOrder.Count;
        while (_cache.Count > MaxCachedSongs && attempts-- > 0)
        {
            var old = _cacheOrder.Dequeue();

            if (IsPlaying(old))
            {
                _cacheOrder.Enqueue(old);
                continue;
            }

            // Disposing frees the OpenAL buffer, dropping the reference alone would not
            if (_cache.Remove(old, out var resource))
                resource.AudioStream.Dispose();
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
