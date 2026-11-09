using Content.Shared._Maid.PlayerMusic;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Maid.PlayerMusic;

/// <summary>
/// Plays boombox songs locally. The audio file is requested from the server only when this client
/// is near a playing boombox and does not have the song yet; downloaded songs are cached in memory.
/// </summary>
public sealed class PlayerMusicSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private const int MaxCachedSongs = 6;
    private static readonly TimeSpan RequestRetry = TimeSpan.FromSeconds(5);
    private static ulong _shareIdx;

    private readonly MemoryContentRoot _contentRoot = new();
    private ResPath _prefix;
    private ulong _fileIdx;

    private readonly Dictionary<string, AudioResource> _cache = new();
    private readonly Queue<string> _cacheOrder = new();
    private readonly Dictionary<string, TimeSpan> _requested = new();
    private readonly Dictionary<EntityUid, PlayingState> _playing = new();

    private readonly record struct PlayingState(
        EntityUid AudioEntity,
        string SongId,
        TimeSpan StartedAt,
        bool Repeat,
        bool Stereo);

    public override void Initialize()
    {
        base.Initialize();

        _prefix = ResPath.Root / $"PlayerMusic{_shareIdx++}";
        _res.AddRoot(_prefix, _contentRoot);

        SubscribeNetworkEvent<PlayerMusicFileEvent>(OnFile);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<PlayerMusicBoomboxComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var state in _playing.Values)
        {
            DeleteAudio(state.AudioEntity);
        }

        _playing.Clear();
        _cache.Clear();
        _contentRoot.Clear();
        _contentRoot.Dispose();
    }

    public void RequestUpload(EntityUid boombox, string name, byte[] data)
    {
        RaiseNetworkEvent(new PlayerMusicUploadEvent(GetNetEntity(boombox), name, data));
    }

    /// <summary>
    /// Stereo audio cannot be positional in OpenAL (the engine only positions mono sources),
    /// so for stereo songs we fade the volume with distance ourselves.
    /// </summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

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

            _audio.SetVolume(state.AudioEntity, comp.Volume + SharedAudioSystem.GainToVolume(Math.Max(gain, 0.0001f)));
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

    private void Sync(Entity<PlayerMusicBoomboxComponent> ent)
    {
        var comp = ent.Comp;

        if (comp.StartedAt is not { } startedAt || comp.SongId is not { } songId)
        {
            StopLocal(ent.Owner);
            return;
        }

        // Already playing this exact run
        if (_playing.TryGetValue(ent.Owner, out var current))
        {
            if (current.SongId == songId && current.StartedAt == startedAt && current.Repeat == comp.Repeat)
                return;

            StopLocal(ent.Owner);
        }

        if (!_cache.TryGetValue(songId, out var resource))
        {
            RequestFile(ent.Owner, songId);
            return;
        }

        var stream = resource.AudioStream;
        var stereo = stream.ChannelCount > 1;

        var offset = (float) (_timing.CurTime - startedAt).TotalSeconds;
        if (offset < 0f)
            offset = 0f;

        var audioParams = AudioParams.Default
            .WithVolume(comp.Volume)
            .WithMaxDistance(comp.MaxDistance)
            .WithLoop(comp.Repeat)
            .WithPlayOffset(offset);

        (EntityUid Entity, Robust.Shared.Audio.Components.AudioComponent Component)? result;
        try
        {
            result = stereo
                ? _audio.PlayGlobal(stream, null, audioParams)
                : _audio.PlayEntity(stream, ent.Owner, null, audioParams);
        }
        catch (Exception e)
        {
            Log.Error($"PlayerMusic: failed to start playback: {e}");
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
        var now = _timing.CurTime;
        if (_requested.TryGetValue(songId, out var last) && now - last < RequestRetry)
            return;

        _requested[songId] = now;
        RaiseNetworkEvent(new PlayerMusicRequestFileEvent(GetNetEntity(boombox), songId));
    }

    private void OnFile(PlayerMusicFileEvent ev)
    {
        if (_cache.ContainsKey(ev.SongId))
            return;

        var filePath = new ResPath($"{_fileIdx++}.ogg");
        _contentRoot.AddOrUpdateFile(filePath, ev.Data);

        var resource = new AudioResource();
        resource.Load(IoCManager.Instance!, _prefix / filePath);

        // The stream is already loaded, the raw bytes are no longer needed
        _contentRoot.RemoveFile(filePath);

        _cache[ev.SongId] = resource;
        _cacheOrder.Enqueue(ev.SongId);
        _requested.Remove(ev.SongId);

        while (_cacheOrder.Count > MaxCachedSongs)
        {
            _cache.Remove(_cacheOrder.Dequeue());
        }

        // Start playback on every boombox that was waiting for this song
        var query = AllEntityQuery<PlayerMusicBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SongId == ev.SongId && comp.StartedAt != null)
                Sync((uid, comp));
        }
    }
}
