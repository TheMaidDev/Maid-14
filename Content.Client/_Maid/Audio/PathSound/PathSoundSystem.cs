using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Maid.Audio;
using Content.Shared._Maid.CVars;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Replaces the engine's single-raycast occlusion — and its whole stream processing — with four
///     weighted Dijkstra tile fields rooted around the local player's position. All loudness shaping
///     (muffling, blocker loss, distance fade) is folded into one occlusion scalar that the engine
///     lowpass carries, so a sound is born at its final loudness and stream volume is never touched.
/// </summary>
public sealed class PathSoundSystem : EntitySystem
{
    /// <summary>Fixture mask matching the engine occlusion raycast (audio occlusion mask is Impassable).</summary>
    private const int ImpassableMask = (int) CollisionGroup.Impassable;

    /// <summary>Layer bit that marks a blocker as fully opaque.</summary>
    private const int OpaqueMask = (int) CollisionGroup.Opaque;

    /// <summary>Layer bit that marks a blocker as glass-like.</summary>
    private const int GlassMask = (int) CollisionGroup.GlassLayer;

    /// <summary>Gain share of the detour path when its folded value is strictly better.</summary>
    private const float DetourShare = 0.5f;

    /// <summary>Minimum field radius in tiles.</summary>
    private const int MinRadius = 4;

    /// <summary>Maximum field radius in tiles.</summary>
    private const int MaxRadius = 32;

    /// <summary>Number of Dijkstra fields rooted around the receiver's tile corners.</summary>
    private const int RootCount = 4;

    /// <summary>Empty-tile sentinel in the weight caches: vacuum carries no detour and no primary weight.</summary>
    private const float TileEmptyWeight = -2f;

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedAudioSystem _sharedAudio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    /// <summary>Broadband attenuation of the engine lowpass per occlusion unit: 20/ln(10) / 10 = 0.8686 dB.</summary>
    private const float OcclDbPerUnit = 0.8686f;

    /// <summary>Whether the feature cvar is enabled.</summary>
    private bool _enabled = true;

    /// <summary>Clamped field radius in tiles.</summary>
    private int _radius = MinRadius;

    /// <summary>Occlusion added per unit of blocking weight.</summary>
    private float _occlusionPerWeight;

    /// <summary>Volume loss in dB per tile of path length.</summary>
    private float _lossDbPerTile;

    /// <summary>Occlusion faded per tile of path past the last blocker.</summary>
    private float _occlDecay;

    /// <summary>Volume lost in dB per unit of applied occlusion.</summary>
    private float _occlLossDb;

    /// <summary>Heuristic weight for opaque blockers.</summary>
    private float _weightOpaque;

    /// <summary>Heuristic weight for glass blockers.</summary>
    private float _weightGlass;

    /// <summary>Seconds before the field is rebuilt.</summary>
    private float _ttl;

    /// <summary>Fraction of the gap to the raw filter value closed per smoothing call.</summary>
    private float _smoothing;

    /// <summary>Blocker occlusion multiplier of the detour pass; 0 disables detours.</summary>
    private float _detourWeight;

    /// <summary>Whether a blocker event requested a field rebuild.</summary>
    private bool _dirty;

    /// <summary>Whether a door event requested a field rebuild without the dirty throttle.</summary>
    private bool _doorDirty;

    private bool _overlayAdded;
    private PathSoundOverlayMode _overlayMode;
    private PathSoundOverlay? _overlay;

    private EntityUid? _fieldGrid;
    private Vector2i _baseTile;
    private int _fieldRadius;
    private TimeSpan _fieldTime;

    /// <summary>Live field arrays of the current build; background rebuilds fill a scratch bundle that is copied over.</summary>
    private FieldBuffers _live = null!;

    /// <summary>Scratch bundles for background builds; two cover one draining build plus one in flight.</summary>
    private readonly FieldBuffers[] _scratch = new FieldBuffers[2];

    private bool _buildInFlight;
    private FieldBuffers? _pendingBuild;
    private readonly System.Diagnostics.Stopwatch _rebuildWatch = new();
    private float _lastRebuildMs;

    /// <summary>Blocking occlusion per grid tile, persistent across rebuilds; TileEmptyWeight marks vacuum.</summary>
    private readonly Dictionary<Vector2i, float> _tileWeightCache = new();

    /// <summary>Grid tiles whose blocking weight may have changed since the last build.</summary>
    private readonly HashSet<Vector2i> _dirtyWeightTiles = new();

    /// <summary>Grid the weight cache belongs to.</summary>
    private EntityUid? _weightCacheGrid;

    /// <summary>Time of the last full weight-cache refresh.</summary>
    private TimeSpan _weightRefreshTime;

    /// <summary>Whether the stream-processing override has been registered by this instance.</summary>
    private bool _overrideSubscribed;

    /// <summary>Smoothed filter occlusion per audio stream, written from the audio job threads (benign races).</summary>
    private readonly ConcurrentDictionary<EntityUid, SmoothEntry> _smoothed = new();

    /// <summary>Last filter values per audio stream for the soundpathdebug dump command and the overlay markers.</summary>
    private readonly ConcurrentDictionary<EntityUid, DebugEntry> _debug = new();

    /// <summary>Monotonic per-call counter stamping smoothed-entry freshness.</summary>
    private long _tickCounter;

    private EntityQuery<SoundPathWeightComponent> _weightQuery;

    private EntityQuery<DoorComponent> _doorQuery;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    /// <summary>Smoothed filter occlusion of one audio stream and the call stamp it was last touched at.</summary>
    private readonly record struct SmoothEntry(float Occl, long LastCall);

    /// <summary>Snapshot of one sound's filter values for the debug dump and overlay.</summary>
    public readonly record struct DebugEntry(float SumWeight, float PathLen, float Occlusion, float Smoothed, float Past, bool Detoured);

    /// <summary>Array set of one field build: the live field or a scratch buffer of a background rebuild.</summary>
    private sealed class FieldBuffers
    {
        public readonly float[] Dist;
        public readonly float[] SumW;
        public readonly int[] Hops;
        public readonly int[] Past;
        public readonly float[] Dist2;
        public readonly float[] SumW2;
        public readonly int[] Hops2;
        public readonly int[] Past2;
        public readonly bool[] Settled;
        public readonly int[] HeapIdx;
        public readonly float[] HeapKey;
        public readonly float[] Window;

        public int HeapCount;
        public bool DetourValid;
        public volatile bool Ready;
        public bool Busy;
        public bool Stale;
        public Vector2i BaseTile;
        public TimeSpan Time;
        public float BuildMs;

        public FieldBuffers(int fieldCount, int heapCount, int window)
        {
            Dist = new float[fieldCount];
            SumW = new float[fieldCount];
            Hops = new int[fieldCount];
            Past = new int[fieldCount];
            Dist2 = new float[fieldCount];
            SumW2 = new float[fieldCount];
            Hops2 = new int[fieldCount];
            Past2 = new int[fieldCount];
            Settled = new bool[fieldCount];
            HeapIdx = new int[heapCount];
            HeapKey = new float[heapCount];
            Window = new float[window];
        }
    }

    /// <summary>Formats the last filter values per sound for the soundpathdebug command.</summary>
    public string DumpDebug()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"smoothed sounds: {_debug.Count}, radius: {_fieldRadius}, grid: {_fieldGrid}, seed: {_baseTile}, enabled: {_enabled}, smooth: {_smoothing:0.00}, rebuild: {_lastRebuildMs:0.0} ms, async: {_buildInFlight}");

        foreach (var (uid, entry) in _debug)
        {
            sb.AppendLine($"{uid} occlSum={entry.SumWeight:0.00} path={entry.PathLen:0.0} past={entry.Past:0} occl={entry.Occlusion:0.00} filter={entry.Smoothed:0.00}{(entry.Detoured ? " *detour" : "")}");
        }

        return sb.ToString();
    }

    /// <summary>Current overlay display mode.</summary>
    public PathSoundOverlayMode OverlayMode
    {
        get => _overlayMode;
        set
        {
            _overlayMode = value;
            var add = value != PathSoundOverlayMode.Off;

            if (add == _overlayAdded)
                return;

            if (add)
                _overlayManager.AddOverlay(_overlay!);
            else
                _overlayManager.RemoveOverlay(_overlay!);

            _overlayAdded = add;
        }
    }

    /// <summary>Whether the debug overlay is currently displayed.</summary>
    public bool OverlayEnabled
    {
        get => OverlayMode != PathSoundOverlayMode.Off;
        set => OverlayMode = value ? PathSoundOverlayMode.Field : PathSoundOverlayMode.Off;
    }

    /// <summary>Current clamped field radius in tiles.</summary>
    public int Radius => _radius;

    /// <summary>Grid the field is currently built on, if any.</summary>
    public EntityUid? FieldGrid => _fieldGrid;

    /// <summary>Base tile of the field window; seed of root 0.</summary>
    public Vector2i FieldSeed => _baseTile;

    /// <summary>Whether the fields are built and not expired.</summary>
    public bool FieldReady => _fieldGrid != null && (float) (_timing.CurTime - _fieldTime).TotalSeconds < _ttl;

    /// <summary>Occlusion added per unit of blocking weight.</summary>
    public float OcclusionPerWeight => _occlusionPerWeight;

    /// <summary>Volume loss in dB per tile of sound path.</summary>
    public float LossDbPerTile => _lossDbPerTile;

    /// <summary>Occlusion faded per tile of path past the last blocker.</summary>
    public float OcclusionDecayPerTile => _occlDecay;

    /// <summary>Volume lost in dB per unit of applied occlusion.</summary>
    public float OcclusionLossDb => _occlLossDb;

    /// <summary>Fraction of the target values applied per update.</summary>
    public float Smoothing => _smoothing;

    /// <summary>Blocker occlusion multiplier of the detour pass; 0 disables detours.</summary>
    public float DetourWeight => _detourWeight;

    /// <summary>Last filter values per sound for the overlay markers.</summary>
    public IReadOnlyDictionary<EntityUid, DebugEntry> DebugEntries => _debug;

    /// <summary>Test and debug read of the model: raw path occlusion and the folded filter value for a world position, without smoothing.</summary>
    public bool TryGetModelOcclusion(MapCoordinates listener, Vector2 sourcePos, out float occl, out float pathLen, out float past, out float filterOccl)
    {
        filterOccl = 0f;

        if (!TryGetAudio(listener, sourcePos, out occl, out _, out pathLen, out past, out _)
            && !TryGetAudio(listener, sourcePos, out occl, out _, out pathLen, out past, out _, true))
            return false;

        var tileSize = 1f;
        if (_fieldGrid is { } grid && TryComp(grid, out MapGridComponent? gridComp))
            tileSize = gridComp.TileSize;

        filterOccl = occl * (1f + _occlLossDb / OcclDbPerUnit)
            + (pathLen + past) * _lossDbPerTile / OcclDbPerUnit
            + MathF.Max(0f, (sourcePos - listener.Position).Length() / tileSize - pathLen) * _lossDbPerTile / OcclDbPerUnit;
        return true;
    }

    /// <summary>Returns the summed blocking occlusion, hop count and past-blocker distance for a grid tile in root 0, if covered and reachable.</summary>
    public bool TryGetTile(Vector2i tile, out float sumOcclusion, out int pathLen, out int past)
    {
        var live = _live;
        if (_fieldGrid != null
            && Math.Abs(tile.X - _baseTile.X) <= _fieldRadius
            && Math.Abs(tile.Y - _baseTile.Y) <= _fieldRadius)
        {
            var size = _fieldRadius * 2 + 1;
            var idx = (tile.X - _baseTile.X + _fieldRadius) + size * (tile.Y - _baseTile.Y + _fieldRadius);
            if (!float.IsPositiveInfinity(live.Dist[idx]))
            {
                sumOcclusion = live.SumW[idx];
                pathLen = live.Hops[idx];
                past = live.Past[idx];
                return true;
            }
        }

        sumOcclusion = 0f;
        pathLen = 0;
        past = 0;
        return false;
    }

    /// <summary>Reconstructs the shortest and detour routes from a sound's tile to the dominant field root, for the route overlay.</summary>
    public bool TryGetRoute(EntityUid sound, List<Vector2i> primary, List<Vector2i> detour)
    {
        primary.Clear();
        detour.Clear();

        if (_fieldGrid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        if (!TryComp(sound, out TransformComponent? xform) || xform.GridUid != gridUid)
            return false;

        var tileSize = grid.TileSize;
        var local = _map.LocalToGrid(gridUid, grid, xform.Coordinates);
        var fx = local.X / tileSize;
        var fy = local.Y / tileSize;
        var srcTile = new Vector2i((int) MathF.Floor(fx), (int) MathF.Floor(fy));

        // The dominant root is the field corner nearest the listener's fractional position.
        var recvAx = 0.5f;
        var recvAy = 0.5f;
        if (_player.LocalEntity is { } player && TryComp(player, out TransformComponent? playerXform)
            && playerXform.GridUid == gridUid)
        {
            var pLocal = _map.LocalToGrid(gridUid, grid, playerXform.Coordinates);
            recvAx = Math.Clamp(pLocal.X / tileSize - MathF.Floor(pLocal.X / tileSize), 0f, 1f);
            recvAy = Math.Clamp(pLocal.Y / tileSize - MathF.Floor(pLocal.Y / tileSize), 0f, 1f);
        }

        var rootDx = recvAx >= 0.5f ? 1 : 0;
        var rootDy = recvAy >= 0.5f ? 1 : 0;
        var size = _fieldRadius * 2 + 1;
        var baseIdx = (rootDx + rootDy * 2) * size * size;

        var offsetX = srcTile.X - _baseTile.X - rootDx;
        var offsetY = srcTile.Y - _baseTile.Y - rootDy;
        if (Math.Abs(offsetX) > _fieldRadius || Math.Abs(offsetY) > _fieldRadius)
            return false;

        var live = _live;
        var idx = baseIdx + (offsetX + _fieldRadius) + size * (offsetY + _fieldRadius);
        if (float.IsPositiveInfinity(live.Dist[idx]))
            return false;

        Descend(live, primary, idx, baseIdx, size, false);
        if (_detourWeight > 0f && live.DetourValid && _debug.TryGetValue(sound, out var entry) && entry.Detoured)
            Descend(live, detour, idx, baseIdx, size, true);

        return primary.Count > 0;
    }

    /// <summary>Walks the precomputed field downhill from a tile to its root seed; the parent always has a strictly smaller cost.</summary>
    private void Descend(FieldBuffers live, List<Vector2i> route, int idx, int baseIdx, int size, bool detour)
    {
        var root = baseIdx / (size * size);
        var rootDx = root & 1;
        var rootDy = root >> 1;
        var radius = _fieldRadius;
        var dist = detour ? live.Dist2 : live.Dist;

        route.Clear();
        var guard = radius * 2 + 2;

        while (true)
        {
            var x = (idx - baseIdx) % size - radius;
            var y = (idx - baseIdx) / size - radius;
            route.Add(new Vector2i(_baseTile.X + rootDx + x, _baseTile.Y + rootDy + y));

            if (dist[idx] <= 0f || guard-- <= 0)
                break;

            var best = -1;
            var bestDist = dist[idx];
            for (var dir = 0; dir < 4; dir++)
            {
                var nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                var ny = y + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                if (nx < -radius || nx > radius || ny < -radius || ny > radius)
                    continue;

                var nIdx = baseIdx + (nx + radius) + size * (ny + radius);
                if (dist[nIdx] < bestDist)
                {
                    bestDist = dist[nIdx];
                    best = nIdx;
                }
            }

            if (best < 0)
                break;

            idx = best;
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MaidCVars.SoundPathEnabled, SetEnabled, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathRadius, v => _radius = Math.Clamp(v, MinRadius, MaxRadius), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathOcclusionPerWeight, v => _occlusionPerWeight = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathLossDbPerTile, v => _lossDbPerTile = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathOcclusionDecayPerTile, v => _occlDecay = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathOcclusionLossDb, v => _occlLossDb = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightOpaque, v => _weightOpaque = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightGlass, v => _weightGlass = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathCacheTtl, v => _ttl = MathF.Max(0.25f, v), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathSmoothing, v => _smoothing = Math.Clamp(v, 0.01f, 1f), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathDetourWeight, OnDetourWeight, true);

        SubscribeLocalEvent<DoorStateChangedEvent>(OnBlockerChanged);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnBlockerChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnBlockerChanged);
        SubscribeLocalEvent<CollisionChangeEvent>(OnBlockerChanged);

        _weightQuery = GetEntityQuery<SoundPathWeightComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SetEnabled(_enabled);

        _overlay = new PathSoundOverlay(this);
    }

    /// <summary>Applies the detour weight cvar and schedules a field rebuild.</summary>
    private void OnDetourWeight(float value)
    {
        _detourWeight = value;
        _dirty = true;
    }

    /// <summary>Registers or releases the stream-processing override; the engine body runs untouched while disabled.</summary>
    private void SetEnabled(bool value)
    {
        _enabled = value;

        if (value && !_overrideSubscribed)
        {
            _audio.ProcessStreamOverride += OnProcessStream;
            _overrideSubscribed = true;
        }
        else if (!value && _overrideSubscribed)
        {
            _audio.ProcessStreamOverride -= OnProcessStream;
            _overrideSubscribed = false;
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        OverlayMode = PathSoundOverlayMode.Off;

        if (_overrideSubscribed)
        {
            _audio.ProcessStreamOverride -= OnProcessStream;
            _overrideSubscribed = false;
        }

        _smoothed.Clear();
        _debug.Clear();
        _fieldGrid = null;
        _dirty = false;
        _doorDirty = false;
        _tileWeightCache.Clear();
        _dirtyWeightTiles.Clear();
        _weightCacheGrid = null;
        _buildInFlight = false;
        _pendingBuild = null;
    }

    /// <summary>Rebuilds the fields the moment a door state change alters a door tile's blocking weight: predicted door sounds spawn in the same tick.</summary>
    private void OnBlockerChanged(DoorStateChangedEvent args)
    {
        if (DoorWeightsUnchanged())
            return;

        _doorDirty = true;
        RebuildNow();
    }

    /// <summary>Checks whether any door tile's cached weight moved with this state change; the directed event carries no entity.</summary>
    private bool DoorWeightsUnchanged()
    {
        if (_weightCacheGrid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        var query = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            var local = _map.LocalToGrid(gridUid, grid, xform.Coordinates);
            var tile = new Vector2i((int) MathF.Floor(local.X / grid.TileSize), (int) MathF.Floor(local.Y / grid.TileSize));

            if (!_tileWeightCache.TryGetValue(tile, out var cached))
                return false;

            var fresh = TileWeightUncached(gridUid, grid, tile);
            if (cached == TileEmptyWeight ? fresh != 0f : cached != fresh)
            {
                _dirtyWeightTiles.Add(tile);
                return false;
            }
        }

        return true;
    }

    /// <summary>Computes a tile's blocking weight straight from the world, empty tiles included.</summary>
    private float TileWeightUncached(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTile(grid, tile, out var tileData) || tileData.IsEmpty)
            return 0f;

        return ComputeTileWeight(gridUid, grid, tile);
    }

    /// <summary>Marks the tile of an entity whose blocking state changed for a weight recompute.</summary>
    private void MarkWeightDirty(EntityUid entity)
    {
        if (_weightCacheGrid is not { } gridUid
            || !TryComp(gridUid, out MapGridComponent? grid)
            || !TryComp(entity, out TransformComponent? xform)
            || xform.GridUid != gridUid)
            return;

        var local = _map.LocalToGrid(gridUid, grid, xform.Coordinates);
        _dirtyWeightTiles.Add(new((int) MathF.Floor(local.X / grid.TileSize), (int) MathF.Floor(local.Y / grid.TileSize)));
    }

    private void MarkWeightDirty(Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (_weightCacheGrid != grid.Owner)
            return;

        _dirtyWeightTiles.Add(tile);
    }

    /// <summary>Rebuilds the fields immediately on the main thread when a player grid is available; the frame update handles the rest.</summary>
    private void RebuildNow()
    {
        if (_player.LocalEntity is not { } player
            || !TryComp(player, out TransformComponent? playerXform)
            || playerXform.GridUid is not { } gridUid
            || !TryComp(gridUid, out MapGridComponent? grid))
        {
            return;
        }

        var tileSize = grid.TileSize;
        var local = _map.LocalToGrid(gridUid, grid, playerXform.Coordinates);
        var baseTile = new Vector2i((int) MathF.Floor(local.X / tileSize), (int) MathF.Floor(local.Y / tileSize));
        RebuildField(gridUid, grid, baseTile);
        _dirty = false;
        _doorDirty = false;
    }

    private void OnBlockerChanged(ref AnchorStateChangedEvent args)
    {
        MarkWeightDirty(args.Entity);
        _dirty = true;
    }

    private void OnBlockerChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
            MarkWeightDirty(args.Entity, change.GridIndices);

        _dirty = true;
    }

    private void OnBlockerChanged(ref CollisionChangeEvent args)
    {
        MarkWeightDirty(args.BodyUid);
        _dirty = true;
    }

    /// <summary>Rebuilds the fields when needed and sweeps stale smoothed entries.</summary>
    public override void FrameUpdate(float frameTime)
    {
        if (!_enabled)
        {
            ResetAll();
            return;
        }

        var player = _player.LocalEntity;
        if (player == null || !TryComp(player.Value, out TransformComponent? playerXform))
        {
            ResetAll();
            return;
        }

        if (playerXform.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
        {
            ResetAll();
            return;
        }

        var tileSize = grid.TileSize;
        var local = _map.LocalToGrid(gridUid, grid, playerXform.Coordinates);
        var fracX = local.X / tileSize;
        var fracY = local.Y / tileSize;
        var baseTile = new Vector2i((int) MathF.Floor(fracX), (int) MathF.Floor(fracY));

        // Adopt a finished background build before anything reads the field this frame.
        if (_buildInFlight && _pendingBuild is { Ready: true } pending)
        {
            if (pending.Stale)
                DiscardPending();
            else
                SwapBuild(pending);
        }

        if (_fieldGrid == null
            || _fieldGrid != gridUid
            || _fieldRadius != _radius
            || _doorDirty)
        {
            RebuildField(gridUid, grid, baseTile);
            _dirty = false;
            _doorDirty = false;
        }
        else if (_baseTile != baseTile
            || (_dirty && (float) (_timing.CurTime - _fieldTime).TotalSeconds > 0.1f)
            || (float) (_timing.CurTime - _fieldTime).TotalSeconds > _ttl)
        {
            // Tile steps and world events rebuild off the main thread; only grid, radius and door
            // changes rebuild synchronously (door sounds spawn the same tick and must see fresh fields).
            if (!_buildInFlight)
            {
                RefreshTileWeights(gridUid, grid, baseTile);
                PostBuild(baseTile);
                _dirty = false;
            }
        }

        SweepSmoothed();
    }

    /// <summary>Tears the fields down; field values are only meaningful on the grid they were built on.</summary>
    private void ResetAll()
    {
        if (_fieldGrid == null)
            return;

        _fieldGrid = null;
    }

    /// <summary>Drops smoothed entries whose stream died or went untouched for several seconds.</summary>
    private void SweepSmoothed()
    {
        var cutoff = _tickCounter - 300;

        foreach (var (uid, entry) in _smoothed)
        {
            if (!Exists(uid) || entry.LastCall < cutoff)
                _smoothed.TryRemove(uid, out _);
        }
    }

    /// <summary>Computes the blended occlusion from the four fields for a sound at the given world position, interpolated across the receiver's tile corners.</summary>
    private bool TryGetAudio(MapCoordinates listener, Vector2 sourcePos, out float occlusion, out float dbgSumW, out float dbgHops, out float dbgPast, out bool detoured, bool clampToField = false)
    {
        occlusion = 0f;
        dbgSumW = 0f;
        dbgHops = 0f;
        dbgPast = 0f;
        detoured = false;

        if (_fieldGrid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        var live = _live;

        var gridXform = Transform(gridUid);
        if (gridXform.MapID != listener.MapId)
            return false;

        var tileSize = grid.TileSize;
        var inv = _transform.GetInvWorldMatrix(gridXform);

        var recvLocal = Vector2.Transform(listener.Position, inv);
        var recvFx = recvLocal.X / tileSize;
        var recvFy = recvLocal.Y / tileSize;
        var recvBx = (int) MathF.Floor(recvFx);
        var recvBy = (int) MathF.Floor(recvFy);
        var recvAx = Math.Clamp(recvFx - recvBx, 0f, 1f);
        var recvAy = Math.Clamp(recvFy - recvBy, 0f, 1f);

        var srcLocal = Vector2.Transform(sourcePos, inv);
        var srcFx = srcLocal.X / tileSize;
        var srcFy = srcLocal.Y / tileSize;
        var srcBx = (int) MathF.Floor(srcFx);
        var srcBy = (int) MathF.Floor(srcFy);
        var srcTile = new Vector2i(srcBx, srcBy);

        var radius = _fieldRadius;
        if (clampToField)
        {
            var clampX = Math.Clamp(srcBx, _baseTile.X - radius, _baseTile.X + radius);
            var clampY = Math.Clamp(srcBy, _baseTile.Y - radius, _baseTile.Y + radius);
            srcTile = new Vector2i(clampX, clampY);
        }

        var size = radius * 2 + 1;
        var baseTile = _baseTile;

        var totalWeight = 0f;
        var cutoffSum = 0f;
        var sumWSum = 0f;
        var hopsSum = 0f;
        var pastSum = 0f;

        var dTiles = Vector2.Distance(new Vector2(srcFx, srcFy), new Vector2(recvFx, recvFy));
        var kOccl = 1f + _occlLossDb / OcclDbPerUnit;
        var kLoss = _lossDbPerTile / OcclDbPerUnit;

        for (var root = 0; root < RootCount; root++)
        {
            var rootDx = root & 1;
            var rootDy = root >> 1;
            var rootWeight = (rootDx == 0 ? 1f - recvAx : recvAx) * (rootDy == 0 ? 1f - recvAy : recvAy);
            if (rootWeight <= 0f)
                continue;

            var offsetX = srcTile.X - baseTile.X - rootDx;
            var offsetY = srcTile.Y - baseTile.Y - rootDy;

            if (offsetX < -radius || offsetX > radius || offsetY < -radius || offsetY > radius)
                continue;

            if (!_map.TryGetTile(grid, srcTile, out _))
                continue;

            var idx = root * size * size + (offsetX + radius) + size * (offsetY + radius);
            if (float.IsPositiveInfinity(live.Dist[idx]))
                continue;

            // A blocker belongs to the path a sound crosses, not to the tile it is born on: the source tile's
            // own weight never muffles that sound, while every blocker the route crosses still does.
            var selfW = srcTile.X == srcBx && srcTile.Y == srcBy
                ? ComputeTileWeight(gridUid, grid, srcTile)
                : 0f;
            var occlR1 = MathF.Max(0f, live.SumW[idx] - selfW);
            var pastR1 = live.SumW[idx] > 0f ? live.Past[idx] : 0;
            occlR1 = MathF.Max(0f, occlR1 - _occlDecay * pastR1);
            var hopsR1 = live.Hops[idx];

            var share = 0f;
            var occlR2 = 0f;
            var pastR2 = 0;
            var hopsR2 = 0;

            if (_detourWeight > 0f && live.DetourValid && !float.IsPositiveInfinity(live.Dist2[idx]))
            {
                occlR2 = MathF.Max(0f, live.SumW2[idx] - selfW);
                pastR2 = live.SumW2[idx] > 0f ? live.Past2[idx] : 0;
                occlR2 = MathF.Max(0f, occlR2 - _occlDecay * pastR2);
                hopsR2 = live.Hops2[idx];

                // Blend only when the detour is strictly better over the full folded value, so clean
                // corridors (both paths equally clean) and sealed rooms (no detour) stay untouched.
                var f1 = occlR1 * kOccl + (hopsR1 + pastR1) * kLoss + MathF.Max(0f, dTiles - hopsR1) * kLoss;
                var f2 = occlR2 * kOccl + (hopsR2 + pastR2) * kLoss + MathF.Max(0f, dTiles - hopsR2) * kLoss;
                if (f2 < f1 - 1e-3f)
                    share = DetourShare;
            }

            totalWeight += rootWeight;
            if (share > 0f)
            {
                cutoffSum += rootWeight * ((1f - share) * MathF.Exp(-occlR1) + share * MathF.Exp(-occlR2));
                sumWSum += rootWeight * ((1f - share) * occlR1 + share * occlR2);
                hopsSum += rootWeight * ((1f - share) * hopsR1 + share * hopsR2);
                pastSum += rootWeight * ((1f - share) * pastR1 + share * pastR2);
                detoured = true;
            }
            else
            {
                cutoffSum += rootWeight * MathF.Exp(-occlR1);
                sumWSum += rootWeight * occlR1;
                hopsSum += rootWeight * hopsR1;
                pastSum += rootWeight * pastR1;
            }
        }

        if (totalWeight <= 0f)
            return false;

        occlusion = -MathF.Log(MathF.Max(cutoffSum / totalWeight, 1e-9f));
        dbgSumW = sumWSum / totalWeight;
        dbgHops = hopsSum / totalWeight;
        dbgPast = pastSum / totalWeight;
        return true;
    }

    /// <summary>Replaces the engine's whole stream processing: same muting, volume reset, max-distance clip, position and velocity as the engine body, but the filter occlusion carries the full path model.</summary>
    private void OnProcessStream(EntityUid entity, AudioComponent component, TransformComponent xform, MapCoordinates listener)
    {
        if (!component.Started)
        {
            component.Started = true;
            component.StartPlaying();
        }

        // Global but on another map: stop playing it.
        if (component.Global)
        {
            if (xform.MapID != MapId.Nullspace && listener.MapId != xform.MapID)
            {
                component.Gain = 0f;
                return;
            }

            component.Volume = component.Params.Volume;
            return;
        }

        // Non-global sounds on another map.
        if (listener.MapId != xform.MapID)
        {
            component.Gain = 0f;
            return;
        }

        var parentUid = xform.ParentUid;
        Vector2 worldPos;
        component.Volume = component.Params.Volume;

        if ((component.Flags & AudioFlags.GridAudio) != 0)
            worldPos = _transform.GetWorldPosition(parentUid);
        else
            worldPos = _transform.GetWorldPosition(xform);

        var delta = worldPos - listener.Position;
        var distance = delta.Length();

        var zOffset = _cfg.GetCVar(CVars.AudioZOffset);
        if (MathF.Sqrt(distance * distance + zOffset * zOffset) > component.MaxDistance)
        {
            component.Gain = 0f;
            return;
        }

        if (distance > 0f && distance < 0.01f)
        {
            worldPos = listener.Position;
            delta = Vector2.Zero;
            distance = 0f;
        }

        if ((component.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion)
        {
            component.Occlusion = 0f;
        }
        else
        {
            component.Occlusion = GetPathOcclusion(entity, xform, listener, delta, distance);
        }

        component.Position = worldPos;

        if (_physicsQuery.TryGetComponent(parentUid, out var physicsComp))
        {
            var velocity = _physics.GetMapLinearVelocity(parentUid, physicsComp);
            component.Velocity = velocity;
        }
    }

    /// <summary>Computes the filter occlusion carrying the whole model: path muffling, blocker loudness loss and the distance fade, all folded into occlusion units.</summary>
    private float GetPathOcclusion(EntityUid entity, TransformComponent xform, MapCoordinates listener, Vector2 delta, float distance)
    {
        var sourcePos = listener.Position + delta;
        var crossGrid = xform.GridUid != _fieldGrid;
        var mask = _sharedAudio.OcclusionCollisionMask | (int) CollisionGroup.GlassAirlockLayer;
        var dir = distance > 0f ? delta / distance : Vector2.Zero;

        float occl = 0f;
        float len = 0f;
        float past = 0f;
        float sumW = 0f;
        var modelled = false;
        var clamped = false;
        var detoured = false;

        if (!crossGrid && TryGetAudio(listener, sourcePos, out occl, out sumW, out len, out past, out detoured))
        {
            modelled = true;
        }
        else
        {
            // Beyond the field: keep the occlusion accumulated to the field edge along the ray and raycast only the rest.
            var edgeDist = _fieldRadius;
            if (_fieldGrid is { } edgeGrid && TryComp(edgeGrid, out MapGridComponent? edgeGridComp))
                edgeDist = _fieldRadius * edgeGridComp.TileSize;

            if (!crossGrid && distance > edgeDist && TryGetAudio(listener, sourcePos, out occl, out sumW, out len, out past, out detoured, true))
            {
                modelled = true;
                clamped = true;
            }
            else
            {
                var rayLength = MathF.Min(distance, _cfg.GetCVar(CVars.AudioRaycastLength));
                var ray = new CollisionRay(listener.Position, dir, mask);
                occl = OcclFromPen(_physics.IntersectRayPenetration(listener.MapId, ray, rayLength, entity));
            }
        }

        // The engine lowpass attenuates 0.8686 dB per occlusion unit, so the loudness-loss terms fold straight in.
        var tileSize = 1f;
        if (_fieldGrid is { } grid && TryComp(grid, out MapGridComponent? gridComp))
            tileSize = gridComp.TileSize;

        var occlValue = occl * (1f + _occlLossDb / OcclDbPerUnit)
            + (len + past) * _lossDbPerTile / OcclDbPerUnit
            + MathF.Max(0f, distance / tileSize - len) * _lossDbPerTile / OcclDbPerUnit;

        // Smooth per stream: rebuild steps fade over a few ticks instead of snapping the filter.
        var tick = Interlocked.Increment(ref _tickCounter);
        var smoothed = _smoothed.AddOrUpdate(entity,
            new SmoothEntry(occlValue, tick),
            (_, old) => new SmoothEntry(old.Occl + (occlValue - old.Occl) * _smoothing, tick)).Occl;

        _debug[entity] = new DebugEntry(sumW, len, occl, smoothed, past, detoured);
        return MathF.Min(smoothed, 400f);
    }

    /// <summary>Converts raycast wall penetration in meters into field occlusion units: one wall per tile of penetration, scaled like a heuristic wall tile.</summary>
    private float OcclFromPen(float pen)
    {
        var tileSize = 1f;
        if (_fieldGrid is { } grid && TryComp(grid, out MapGridComponent? comp))
            tileSize = comp.TileSize;

        return MathF.Round(pen / tileSize) * _occlusionPerWeight * _weightOpaque;
    }

    /// <summary>Allocates the live and scratch field bundles for the current radius; any background build is abandoned.</summary>
    private void AllocateField(int radius)
    {
        var size = radius * 2 + 1;
        var count = size * size;
        var fieldCount = count * RootCount;
        var window = (radius * 2 + 3) * (radius * 2 + 3);

        _live = new FieldBuffers(fieldCount, count * 4, window);
        for (var i = 0; i < _scratch.Length; i++)
            _scratch[i] = new FieldBuffers(fieldCount, count * 4, window);

        _buildInFlight = false;
        _pendingBuild = null;
        _fieldRadius = radius;
    }

    /// <summary>Builds the four weighted Dijkstra fields synchronously into the live buffers: grid, radius and door changes need fields within the same tick.</summary>
    private void RebuildField(EntityUid gridUid, MapGridComponent grid, Vector2i baseTile)
    {
        _rebuildWatch.Restart();

        if (_fieldRadius != _radius)
            AllocateField(_radius);

        var radius = _fieldRadius;
        var size = radius * 2 + 1;

        _fieldGrid = gridUid;
        _baseTile = baseTile;
        _fieldTime = _timing.CurTime;

        DropPendingBuilds();
        RefreshTileWeights(gridUid, grid, baseTile);

        for (var root = 0; root < RootCount; root++)
        {
            var seed = new Vector2i(baseTile.X + (root & 1), baseTile.Y + (root >> 1));
            BuildRootPrimary(_live, seed, baseTile, root * size * size, size, radius);
        }

        _live.DetourValid = false;
        if (_detourWeight > 0f && WindowHasBlockers(_live))
        {
            for (var root = 0; root < RootCount; root++)
            {
                var seed = new Vector2i(baseTile.X + (root & 1), baseTile.Y + (root >> 1));
                BuildRootDetour(_live, seed, baseTile, root * size * size, size, radius);
            }

            _live.DetourValid = true;
        }

        _rebuildWatch.Stop();
        _lastRebuildMs = (float) _rebuildWatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Runs the Dijkstra passes into a buffer; pure array math, safe off the main thread.</summary>
    private void BuildField(FieldBuffers buffers, Vector2i baseTile, int radius, float detourWeight)
    {
        var size = radius * 2 + 1;

        for (var root = 0; root < RootCount; root++)
        {
            var seed = new Vector2i(baseTile.X + (root & 1), baseTile.Y + (root >> 1));
            BuildRootPrimary(buffers, seed, baseTile, root * size * size, size, radius);
        }

        buffers.DetourValid = false;
        if (detourWeight > 0f && WindowHasBlockers(buffers))
        {
            for (var root = 0; root < RootCount; root++)
            {
                var seed = new Vector2i(baseTile.X + (root & 1), baseTile.Y + (root >> 1));
                BuildRootDetour(buffers, seed, baseTile, root * size * size, size, radius);
            }

            buffers.DetourValid = true;
        }
    }

    /// <summary>Refreshes the persistent tile-weight cache (full on grid change or TTL, dirty tiles otherwise) and fills the live window array.</summary>
    private void RefreshTileWeights(EntityUid gridUid, MapGridComponent grid, Vector2i baseTile)
    {
        if (_weightCacheGrid != gridUid
            || (float) (_timing.CurTime - _weightRefreshTime).TotalSeconds > _ttl)
        {
            _tileWeightCache.Clear();
            _dirtyWeightTiles.Clear();
            _weightCacheGrid = gridUid;
        }

        _weightRefreshTime = _timing.CurTime;

        foreach (var tile in _dirtyWeightTiles)
            _tileWeightCache.Remove(tile);
        _dirtyWeightTiles.Clear();

        var radius = _fieldRadius;
        var w = radius * 2 + 3;

        for (var gx = -radius - 1; gx <= radius + 1; gx++)
        {
            for (var gy = -radius - 1; gy <= radius + 1; gy++)
            {
                var tile = new Vector2i(baseTile.X + gx, baseTile.Y + gy);
                if (!_tileWeightCache.TryGetValue(tile, out var weight))
                {
                    if (!_map.TryGetTile(grid, tile, out var tileData) || tileData.IsEmpty)
                        weight = TileEmptyWeight;
                    else
                        weight = ComputeTileWeight(gridUid, grid, tile);

                    _tileWeightCache[tile] = weight;
                }

                _live.Window[(gx + radius + 1) + w * (gy + radius + 1)] = weight;
            }
        }
    }

    /// <summary>Posts a background field build into a free scratch buffer; the worker only touches arrays.</summary>
    private void PostBuild(Vector2i baseTile)
    {
        FieldBuffers? target = null;
        foreach (var scratch in _scratch)
        {
            if (scratch.Busy)
                continue;
            target = scratch;
            break;
        }

        if (target == null)
            return;

        var radius = _fieldRadius;
        var detourWeight = _detourWeight;

        target.Busy = true;
        target.Stale = false;
        target.Ready = false;
        target.BaseTile = baseTile;
        target.Time = _timing.CurTime;
        Array.Copy(_live.Window, target.Window, _live.Window.Length);
        _pendingBuild = target;
        _buildInFlight = true;

        Task.Run(() =>
        {
            try
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildField(target, baseTile, radius, detourWeight);
                target.BuildMs = (float) ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                target.Ready = true;
            }
            catch
            {
                target.Ready = false;
                target.Busy = false;
                if (_pendingBuild == target)
                {
                    _buildInFlight = false;
                    _pendingBuild = null;
                }
            }
        });
    }

    /// <summary>Adopts a finished background build into the live buffers.</summary>
    private void SwapBuild(FieldBuffers pending)
    {
        Array.Copy(pending.Dist, _live.Dist, pending.Dist.Length);
        Array.Copy(pending.SumW, _live.SumW, pending.SumW.Length);
        Array.Copy(pending.Hops, _live.Hops, pending.Hops.Length);
        Array.Copy(pending.Past, _live.Past, pending.Past.Length);
        Array.Copy(pending.Dist2, _live.Dist2, pending.Dist2.Length);
        Array.Copy(pending.SumW2, _live.SumW2, pending.SumW2.Length);
        Array.Copy(pending.Hops2, _live.Hops2, pending.Hops2.Length);
        Array.Copy(pending.Past2, _live.Past2, pending.Past2.Length);
        _live.DetourValid = pending.DetourValid;
        _baseTile = pending.BaseTile;
        _fieldTime = pending.Time;
        _lastRebuildMs = pending.BuildMs;

        _buildInFlight = false;
        _pendingBuild = null;
        pending.Ready = false;
        pending.Busy = false;
        pending.Stale = false;
    }

    /// <summary>Drops a finished-but-stale background build.</summary>
    private void DiscardPending()
    {
        if (_pendingBuild is not { } pending)
            return;

        _buildInFlight = false;
        _pendingBuild = null;
        pending.Ready = false;
        pending.Busy = false;
        pending.Stale = false;
    }

    /// <summary>Abandons any background build; its scratch stays busy until the worker drains.</summary>
    private void DropPendingBuilds()
    {
        if (!_buildInFlight)
            return;

        _buildInFlight = false;
        if (_pendingBuild is { } pending)
            pending.Stale = true;
        _pendingBuild = null;
    }

    /// <summary>Builds the shortest-path field of one root into the given buffers.</summary>
    private void BuildRootPrimary(FieldBuffers buffers, Vector2i seed, Vector2i baseTile, int baseIdx, int size, int radius)
    {
        var count = size * size;

        Array.Fill(buffers.Dist, float.PositiveInfinity, baseIdx, count);
        Array.Fill(buffers.SumW, 0f, baseIdx, count);
        Array.Fill(buffers.Hops, 0, baseIdx, count);
        Array.Fill(buffers.Past, 0, baseIdx, count);
        Array.Fill(buffers.Settled, false, baseIdx, count);

        RunDijkstra(buffers, seed, baseTile, baseIdx, size, radius, false);
    }

    /// <summary>Builds the detour field of one root into the given buffers.</summary>
    private void BuildRootDetour(FieldBuffers buffers, Vector2i seed, Vector2i baseTile, int baseIdx, int size, int radius)
    {
        var count = size * size;

        Array.Fill(buffers.Dist2, float.PositiveInfinity, baseIdx, count);
        Array.Fill(buffers.SumW2, 0f, baseIdx, count);
        Array.Fill(buffers.Hops2, 0, baseIdx, count);
        Array.Fill(buffers.Past2, 0, baseIdx, count);
        Array.Fill(buffers.Settled, false, baseIdx, count);

        RunDijkstra(buffers, seed, baseTile, baseIdx, size, radius, true);
    }

    /// <summary>Runs the Dijkstra heap loop over the primary or detour field arrays.</summary>
    private void RunDijkstra(FieldBuffers buffers, Vector2i seed, Vector2i baseTile, int baseIdx, int size, int radius, bool detour)
    {
        buffers.HeapCount = 0;

        var seedIdx = baseIdx + radius + size * radius;
        if (detour)
            buffers.Dist2[seedIdx] = 0f;
        else
            buffers.Dist[seedIdx] = 0f;
        Push(buffers, seedIdx, 0f);

        while (buffers.HeapCount > 0)
        {
            var idx = PopMin(buffers);
            if (buffers.Settled[idx])
                continue;

            buffers.Settled[idx] = true;

            var x = (idx - baseIdx) % size - radius;
            var y = (idx - baseIdx) / size - radius;

            Relax(buffers, seed, baseTile, baseIdx, size, radius, idx, x + 1, y, detour);
            Relax(buffers, seed, baseTile, baseIdx, size, radius, idx, x - 1, y, detour);
            Relax(buffers, seed, baseTile, baseIdx, size, radius, idx, x, y + 1, detour);
            Relax(buffers, seed, baseTile, baseIdx, size, radius, idx, x, y - 1, detour);
        }
    }

    /// <summary>Relaxes one neighbor tile of a settled node on the primary or detour pass.</summary>
    private void Relax(FieldBuffers buffers, Vector2i seed, Vector2i baseTile, int baseIdx, int size, int radius, int parentIdx, int nx, int ny, bool detour)
    {
        if (nx < -radius || nx > radius || ny < -radius || ny > radius)
            return;

        var idx = baseIdx + (nx + radius) + size * (ny + radius);
        if (buffers.Settled[idx])
            return;

        var tile = new Vector2i(seed.X + nx, seed.Y + ny);
        var wIdx = WeightIndex(tile, baseTile, radius);

        // Vacuum carries no detour: the clean-route pass excludes empty tiles, so it can never
        // circumvent blockers through space the way the shortest pass is allowed to.
        if (detour && buffers.Window[wIdx] == TileEmptyWeight)
            return;

        var weight = TileWeight(buffers, wIdx);
        if (detour)
        {
            var dist2 = buffers.Dist2[parentIdx] + 1f + weight * _detourWeight;
            if (dist2 >= buffers.Dist2[idx])
                return;

            buffers.Dist2[idx] = dist2;
            buffers.Hops2[idx] = buffers.Hops2[parentIdx] + 1;
            buffers.SumW2[idx] = buffers.SumW2[parentIdx] + weight;
            buffers.Past2[idx] = weight > 0f ? 0 : buffers.Past2[parentIdx] + 1;
        }
        else
        {
            var dist = buffers.Dist[parentIdx] + 1f + weight;
            if (dist >= buffers.Dist[idx])
                return;

            buffers.Dist[idx] = dist;
            buffers.Hops[idx] = buffers.Hops[parentIdx] + 1;
            buffers.SumW[idx] = buffers.SumW[parentIdx] + weight;
            buffers.Past[idx] = weight > 0f ? 0 : buffers.Past[parentIdx] + 1;
        }

        Push(buffers, idx, detour ? buffers.Dist2[idx] : buffers.Dist[idx]);
    }

    /// <summary>Returns the blocking occlusion of a tile from the buffer's window cache; vacuum weighs zero.</summary>
    private float TileWeight(FieldBuffers buffers, int wIdx)
    {
        var weight = buffers.Window[wIdx];
        return weight >= 0f ? weight : 0f;
    }

    /// <summary>Index of a grid tile in the shared window array centered on the given base tile.</summary>
    private int WeightIndex(Vector2i tile, Vector2i baseTile, int radius)
    {
        var w = radius * 2 + 3;
        return tile.X - baseTile.X + radius + 1 + w * (tile.Y - baseTile.Y + radius + 1);
    }

    /// <summary>Checks whether the tile window contains any blocker at all; blocker-free fields skip the detour passes.</summary>
    private static bool WindowHasBlockers(FieldBuffers buffers)
    {
        foreach (var weight in buffers.Window)
        {
            if (weight > 0f)
                return true;
        }

        return false;
    }

    /// <summary>Pushes a node onto the buffer's preallocated min-heap.</summary>
    private void Push(FieldBuffers buffers, int idx, float key)
    {
        var i = buffers.HeapCount++;
        buffers.HeapIdx[i] = idx;
        buffers.HeapKey[i] = key;

        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (buffers.HeapKey[parent] <= buffers.HeapKey[i])
                break;

            (buffers.HeapKey[parent], buffers.HeapKey[i]) = (buffers.HeapKey[i], buffers.HeapKey[parent]);
            (buffers.HeapIdx[parent], buffers.HeapIdx[i]) = (buffers.HeapIdx[i], buffers.HeapIdx[parent]);
            i = parent;
        }
    }

    /// <summary>Pops the minimum-key node from the buffer's preallocated min-heap.</summary>
    private int PopMin(FieldBuffers buffers)
    {
        var top = buffers.HeapIdx[0];
        buffers.HeapCount--;

        if (buffers.HeapCount > 0)
        {
            buffers.HeapIdx[0] = buffers.HeapIdx[buffers.HeapCount];
            buffers.HeapKey[0] = buffers.HeapKey[buffers.HeapCount];

            var i = 0;
            while (true)
            {
                var left = 2 * i + 1;
                if (left >= buffers.HeapCount)
                    break;

                var child = left;
                var right = left + 1;
                if (right < buffers.HeapCount && buffers.HeapKey[right] < buffers.HeapKey[left])
                    child = right;

                if (buffers.HeapKey[i] <= buffers.HeapKey[child])
                    break;

                (buffers.HeapKey[i], buffers.HeapKey[child]) = (buffers.HeapKey[child], buffers.HeapKey[i]);
                (buffers.HeapIdx[i], buffers.HeapIdx[child]) = (buffers.HeapIdx[child], buffers.HeapIdx[i]);
                i = child;
            }
        }

        return top;
    }

    /// <summary>Computes the blocking occlusion of a tile from its anchored entities: component entities use their muffle percent, static blockers use heuristics.</summary>
    private float ComputeTileWeight(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTile(grid, tile, out var tileData) || tileData.IsEmpty)
            return 0f;

        var best = 0f;
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            float occl;

            var blocked = TryComp(ent.Value, out PhysicsComponent? phys) && phys.CanCollide;
            if (!blocked && _doorQuery.TryGetComponent(ent.Value, out var door)
                && door.State is DoorState.Closing or DoorState.Opening)
            {
                // A door mid-animation still blocks its tile even before its collision flips.
                blocked = true;
            }

            if (!blocked)
                continue;

            if (_weightQuery.TryGetComponent(ent.Value, out var weightComp))
            {
                occl = OcclFromMuffle(weightComp.Muffle);
            }
            else
            {
                if (!TryComp(ent.Value, out FixturesComponent? fixtures))
                    continue;

                var layer = 0;
                foreach (var fixture in fixtures.Fixtures.Values)
                {
                    if (!fixture.Hard)
                        continue;

                    if ((fixture.CollisionLayer & ImpassableMask) == 0
                        && fixture.CollisionLayer != (int) CollisionGroup.AirlockLayer
                        && fixture.CollisionLayer != (int) CollisionGroup.GlassAirlockLayer)
                        continue;

                    layer |= fixture.CollisionLayer;
                }

                if (layer == 0)
                    continue;

                if ((layer & OpaqueMask) != 0)
                    occl = _occlusionPerWeight * _weightOpaque;
                else if ((layer & GlassMask) != 0)
                    occl = _occlusionPerWeight * _weightGlass;
                else
                    occl = _occlusionPerWeight * _weightOpaque;
            }

            if (occl > best)
                best = occl;
        }

        return best;
    }

    /// <summary>Converts a muffling percent to occlusion units: 90 -> ~2.3 (a tenth of highs passes).</summary>
    private static float OcclFromMuffle(float muffle)
    {
        return -MathF.Log(1f - Math.Clamp(muffle, 0f, 99.9f) / 100f);
    }
}
