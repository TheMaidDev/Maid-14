using System.Numerics;
using System.Text;
using Content.Shared._Maid.Audio;
using Content.Shared._Maid.CVars;
using Content.Shared.Physics;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Replaces the engine's single-raycast occlusion with four weighted Dijkstra tile fields rooted
///     around the local player's position; per-root results are blended in the gain domain and smoothed
///     over time before reaching the engine's lowpass filter and volume.
/// </summary>
public sealed class PathSoundSystem : EntitySystem
{
    /// <summary>Fixture mask matching the engine occlusion raycast (audio occlusion mask is Impassable).</summary>
    private const int ImpassableMask = (int) CollisionGroup.Impassable;

    /// <summary>Layer bit that marks a blocker as fully opaque.</summary>
    private const int OpaqueMask = (int) CollisionGroup.Opaque;

    /// <summary>Layer bit that marks a blocker as glass-like.</summary>
    private const int GlassMask = (int) CollisionGroup.GlassLayer;

    /// <summary>Minimum field radius in tiles.</summary>
    private const int MinRadius = 4;

    /// <summary>Maximum field radius in tiles.</summary>
    private const int MaxRadius = 32;

    /// <summary>Number of Dijkstra fields rooted around the receiver's tile corners.</summary>
    private const int RootCount = 4;

    /// <summary>Number of smoothing slots for the occlusion override.</summary>
    private const int SmoothSlots = 128;

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedAudioSystem _sharedAudio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    /// <summary>Whether the feature cvar is enabled.</summary>
    private bool _enabled = true;

    /// <summary>Clamped field radius in tiles.</summary>
    private int _radius = MinRadius;

    /// <summary>Maximum number of sounds managed per pass.</summary>
    private int _maxSources;

    /// <summary>Occlusion added per unit of blocking weight.</summary>
    private float _occlusionPerWeight;

    /// <summary>Volume loss in dB per tile of path detour.</summary>
    private float _lossDbPerTile;

    /// <summary>Occlusion faded per tile of path past the last blocker.</summary>
    private float _occlDecay;

    /// <summary>Heuristic weight for opaque blockers.</summary>
    private float _weightOpaque;

    /// <summary>Heuristic weight for glass blockers.</summary>
    private float _weightGlass;

    /// <summary>Seconds before the field is rebuilt.</summary>
    private float _ttl;

    /// <summary>Fraction of the target values applied per update.</summary>
    private float _smoothing;

    /// <summary>Interval between post-passes in seconds, synced to the engine audio tick.</summary>
    private float _updateInterval = 1f / 30f;

    /// <summary>Accumulated frame time towards the next post-pass.</summary>
    private float _accum;

    private bool _overlayAdded;
    private PathSoundOverlayMode _overlayMode;
    private PathSoundOverlay? _overlay;

    private EntityUid? _fieldGrid;
    private Vector2i _baseTile;
    private int _fieldRadius;
    private TimeSpan _fieldTime;

    /// <summary>Path cost per root field tile, indexed root*count + (x+R) + W*(y+R).</summary>
    private float[] _dist = Array.Empty<float>();

    /// <summary>Accumulated blocking occlusion per root field tile.</summary>
    private float[] _sumW = Array.Empty<float>();

    /// <summary>Path hop count per root field tile.</summary>
    private int[] _hops = Array.Empty<int>();

    /// <summary>Path tiles past the last blocker per root field tile.</summary>
    private int[] _past = Array.Empty<int>();

    /// <summary>Dijkstra settled flag per root field tile.</summary>
    private bool[] _settled = Array.Empty<bool>();

    /// <summary>Cached tile occlusion per root field tile, -1 while unset.</summary>
    private float[] _tileW = Array.Empty<float>();

    private int[] _heapIdx = Array.Empty<int>();
    private float[] _heapKey = Array.Empty<float>();
    private int _heapCount;

    /// <summary>Whether the engine occlusion override has been registered by this instance.</summary>
    private bool _overrideSubscribed;

    /// <summary>Quantized source positions already seen by the occlusion override.</summary>
    private readonly Vector2[] _smoothPos = new Vector2[SmoothSlots];

    /// <summary>Smoothed occlusion value per override slot.</summary>
    private readonly float[] _smoothOcc = new float[SmoothSlots];

    /// <summary>Round-robin cursor for override smoothing slot allocation.</summary>
    private int _smoothNext;

    /// <summary>Smoothed applied volume per managed audio stream.</summary>
    private readonly Dictionary<EntityUid, float> _smoothVol = new();

    /// <summary>Reused removal buffer for dead volume-smoothing entries.</summary>
    private readonly List<EntityUid> _volSweep = new();

    /// <summary>Reused candidate list for the post-pass.</summary>
    private readonly List<SoundCandidate> _candidates = new();

    /// <summary>Last applied per-sound values for the soundpathdebug dump command and the overlay markers.</summary>
    private readonly Dictionary<EntityUid, DebugEntry> _debug = new();

    private EntityQuery<SoundPathWeightComponent> _weightQuery;

    /// <summary>One candidate audio stream for the post-pass.</summary>
    private readonly record struct SoundCandidate(EntityUid Uid, AudioComponent Comp, TransformComponent Xform, float SourceX, float SourceY, float Distance);

    /// <summary>Snapshot of one managed sound's applied values for the debug dump and overlay.</summary>
    public readonly record struct DebugEntry(float SumWeight, float PathLen, float Occlusion, float Volume, float VolumeDelta, float Past, bool Written);

    /// <summary>Formats the last post-pass values of managed sounds for the soundpathdebug command.</summary>
    public string DumpDebug()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"managed sounds: {_debug.Count}, radius: {_fieldRadius}, grid: {_fieldGrid}, seed: {_baseTile}, enabled: {_enabled}, smooth: {_smoothing:0.00}");

        foreach (var (uid, entry) in _debug)
        {
            sb.AppendLine($"{uid} occlSum={entry.SumWeight:0.00} path={entry.PathLen:0.0} past={entry.Past:0} occl={entry.Occlusion:0.00} vol={entry.Volume:0.0} delta={entry.VolumeDelta:+0.0;-0.0} written={entry.Written}");
        }

        return sb.ToString();
    }

    /// <summary>Distance comparison for candidate sorting.</summary>
    private static readonly Comparison<SoundCandidate> SortByDistance = (a, b) => a.Distance.CompareTo(b.Distance);

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

    /// <summary>Volume loss in dB per tile of path detour.</summary>
    public float LossDbPerTile => _lossDbPerTile;

    /// <summary>Occlusion faded per tile of path past the last blocker.</summary>
    public float OcclusionDecayPerTile => _occlDecay;

    /// <summary>Fraction of the target values applied per update.</summary>
    public float Smoothing => _smoothing;

    /// <summary>Last post-pass snapshot of managed sounds.</summary>
    public IReadOnlyDictionary<EntityUid, DebugEntry> DebugEntries => _debug;

    /// <summary>Returns the summed blocking occlusion, hop count and past-blocker distance for a grid tile in root 0, if covered and reachable.</summary>
    public bool TryGetTile(Vector2i tile, out float sumOcclusion, out int pathLen, out int past)
    {
        if (_fieldGrid != null
            && Math.Abs(tile.X - _baseTile.X) <= _fieldRadius
            && Math.Abs(tile.Y - _baseTile.Y) <= _fieldRadius)
        {
            var size = _fieldRadius * 2 + 1;
            var idx = (tile.X - _baseTile.X + _fieldRadius) + size * (tile.Y - _baseTile.Y + _fieldRadius);
            if (!float.IsPositiveInfinity(_dist[idx]))
            {
                sumOcclusion = _sumW[idx];
                pathLen = _hops[idx];
                past = _past[idx];
                return true;
            }
        }

        sumOcclusion = 0f;
        pathLen = 0;
        past = 0;
        return false;
    }

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AudioSystem));

        Subs.CVar(_cfg, MaidCVars.SoundPathEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathRadius, v => _radius = Math.Clamp(v, MinRadius, MaxRadius), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathMaxSources, v => _maxSources = Math.Max(0, v), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathOcclusionPerWeight, v => _occlusionPerWeight = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathLossDbPerTile, v => _lossDbPerTile = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathOcclusionDecayPerTile, v => _occlDecay = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightOpaque, v => _weightOpaque = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightGlass, v => _weightGlass = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathCacheTtl, v => _ttl = MathF.Max(0.25f, v), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathSmoothing, v => _smoothing = Math.Clamp(v, 0.01f, 1f), true);
        Subs.CVar(_cfg, CVars.AudioTickRate, v => _updateInterval = v > 0 ? 1f / v : 1f / 30f, true);

        _weightQuery = GetEntityQuery<SoundPathWeightComponent>();

        if (!_overrideSubscribed)
        {
            _audio.GetOcclusionOverride += OnGetOcclusionOverride;
            _overrideSubscribed = true;
        }

        _overlay = new PathSoundOverlay(this);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        OverlayMode = PathSoundOverlayMode.Off;

        if (_overrideSubscribed)
        {
            _audio.GetOcclusionOverride -= OnGetOcclusionOverride;
            _overrideSubscribed = false;
        }

        _smoothVol.Clear();
        _debug.Clear();
        Array.Clear(_smoothOcc);
        _smoothNext = 0;
        _fieldGrid = null;
    }

    /// <summary>Rebuilds the fields when needed and runs the managed-sound post-pass at the engine audio tick rate.</summary>
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

        if (_fieldGrid != gridUid
            || _baseTile != baseTile
            || _fieldRadius != _radius
            || (float) (_timing.CurTime - _fieldTime).TotalSeconds > _ttl)
        {
            RebuildField(gridUid, grid, baseTile);
        }

        _accum += frameTime;
        if (_accum < _updateInterval)
            return;

        _accum -= _updateInterval;
        if (_accum > _updateInterval)
            _accum = 0f;

        ProcessSounds(gridUid, grid);
    }

    /// <summary>Tears the fields down; field values are only meaningful on the grid they were built on.</summary>
    private void ResetAll()
    {
        if (_fieldGrid == null)
            return;

        _fieldGrid = null;
        _accum = 0f;
    }

    /// <summary>Computes the blended occlusion and volume change from the four fields for a sound at the given world position.</summary>
    private bool TryGetAudio(MapCoordinates listener, Vector2 sourcePos, out float occlusion, out float volDb, out float dbgSumW, out float dbgHops, out float dbgPast)
    {
        occlusion = 0f;
        volDb = 0f;
        dbgSumW = 0f;
        dbgHops = 0f;
        dbgPast = 0f;

        if (_fieldGrid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

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

        var manhattan = (MathF.Abs(sourcePos.X - listener.Position.X) + MathF.Abs(sourcePos.Y - listener.Position.Y)) / tileSize;

        var radius = _fieldRadius;
        var size = radius * 2 + 1;
        var baseTile = _baseTile;
        var nearSrc = Math.Abs(srcBx - baseTile.X) <= 1 && Math.Abs(srcBy - baseTile.Y) <= 1;

        var rootCutoff = 0f;
        var rootVolDb = 0f;
        var rootSumW = 0f;
        var rootHops = 0f;
        var rootPast = 0f;
        var rootWeightSum = 0f;

        for (var root = 0; root < RootCount; root++)
        {
            var rootDx = root & 1;
            var rootDy = root >> 1;
            var rootWeight = (rootDx == 0 ? 1f - recvAx : recvAx) * (rootDy == 0 ? 1f - recvAy : recvAy);

            var offsetX = srcBx - baseTile.X - rootDx;
            var offsetY = srcBy - baseTile.Y - rootDy;

            if (offsetX < -radius || offsetX > radius || offsetY < -radius || offsetY > radius)
                continue;

            if (!_map.TryGetTile(grid, srcTile, out _))
                continue;

            var idx = root * size * size + (offsetX + radius) + size * (offsetY + radius);
            var selfW = nearSrc ? TileWeight(gridUid, grid, srcTile, idx) : 0f;
            var occlR = MathF.Max(0f, _sumW[idx] - selfW);
            var pastR = _sumW[idx] > 0f ? _past[idx] : 0;
            occlR = MathF.Max(0f, occlR - _occlDecay * pastR);
            var deltaR = MathF.Max(0f, _hops[idx] - manhattan);

            rootCutoff += rootWeight * MathF.Exp(-occlR);
            rootVolDb += rootWeight * (-_lossDbPerTile * (deltaR + pastR));
            rootSumW += rootWeight * occlR;
            rootHops += rootWeight * _hops[idx];
            rootPast += rootWeight * pastR;
            rootWeightSum += rootWeight;
        }

        if (rootWeightSum <= 0f)
            return false;

        occlusion = -MathF.Log(MathF.Max(rootCutoff / rootWeightSum, 1e-9f));
        volDb = rootVolDb / rootWeightSum;
        dbgSumW = rootSumW / rootWeightSum;
        dbgHops = rootHops / rootWeightSum;
        dbgPast = rootPast / rootWeightSum;
        return true;
    }

    /// <summary>Engine occlusion override: smoothed field value for sounds inside the field, engine raycast otherwise.</summary>
    private float OnGetOcclusionOverride(MapCoordinates listener, Vector2 delta, float distance, EntityUid? ignoredEnt)
    {
        if (distance <= 0.1f)
            return 0f;

        var sourcePos = listener.Position + delta;

        if (TryGetAudio(listener, sourcePos, out var occlusion, out _, out _, out _, out _))
            return SmoothOverride(sourcePos, occlusion);

        var rayLength = MathF.Min(distance, _cfg.GetCVar(CVars.AudioRaycastLength));
        var mask = _sharedAudio.OcclusionCollisionMask | (int) CollisionGroup.GlassAirlockLayer;
        var ray = new CollisionRay(listener.Position, delta / distance, mask);
        return _physics.IntersectRayPenetration(listener.MapId, ray, rayLength, ignoredEnt);
    }

    /// <summary>Glides the field occlusion towards its target in a per-position slot; same-position sounds share a slot.</summary>
    private float SmoothOverride(Vector2 sourcePos, float target)
    {
        var quantized = new Vector2(MathF.Round(sourcePos.X), MathF.Round(sourcePos.Y));

        for (var i = 0; i < SmoothSlots; i++)
        {
            if (( _smoothPos[i] - quantized).LengthSquared() > 0.01f)
                continue;

            _smoothOcc[i] += (target - _smoothOcc[i]) * _smoothing;
            return _smoothOcc[i];
        }

        var slot = _smoothNext;
        _smoothNext = (_smoothNext + 1) % SmoothSlots;
        _smoothPos[slot] = quantized;
        _smoothOcc[slot] = target;
        return target;
    }

    /// <summary>Allocates the per-root field and shared heap arrays for the current radius.</summary>
    private void AllocateField(int radius)
    {
        var size = radius * 2 + 1;
        var count = size * size;

        _dist = new float[count * RootCount];
        _sumW = new float[count * RootCount];
        _hops = new int[count * RootCount];
        _past = new int[count * RootCount];
        _settled = new bool[count * RootCount];
        _tileW = new float[count * RootCount];
        _heapIdx = new int[count * 4];
        _heapKey = new float[count * 4];
        _fieldRadius = radius;
    }

    /// <summary>Builds the four weighted Dijkstra fields rooted at the tiles around the receiver's base tile.</summary>
    private void RebuildField(EntityUid gridUid, MapGridComponent grid, Vector2i baseTile)
    {
        if (_fieldRadius != _radius)
            AllocateField(_radius);

        var radius = _fieldRadius;
        var size = radius * 2 + 1;
        var count = size * size;

        _fieldGrid = gridUid;
        _baseTile = baseTile;
        _fieldTime = _timing.CurTime;

        for (var root = 0; root < RootCount; root++)
        {
            var seed = new Vector2i(baseTile.X + (root & 1), baseTile.Y + (root >> 1));
            BuildRoot(gridUid, grid, seed, root * count, size, radius);
        }
    }

    /// <summary>Builds one Dijkstra field rooted at the given seed tile.</summary>
    private void BuildRoot(EntityUid gridUid, MapGridComponent grid, Vector2i seed, int baseIdx, int size, int radius)
    {
        var count = size * size;

        Array.Fill(_dist, float.PositiveInfinity, baseIdx, count);
        Array.Fill(_sumW, 0f, baseIdx, count);
        Array.Fill(_hops, 0, baseIdx, count);
        Array.Fill(_past, 0, baseIdx, count);
        Array.Fill(_settled, false, baseIdx, count);
        Array.Fill(_tileW, -1f, baseIdx, count);
        _heapCount = 0;

        var seedIdx = baseIdx + radius + size * radius;
        _dist[seedIdx] = 0f;
        Push(seedIdx, 0f);

        while (_heapCount > 0)
        {
            var idx = PopMin();
            if (_settled[idx])
                continue;

            _settled[idx] = true;

            var x = (idx - baseIdx) % size - radius;
            var y = (idx - baseIdx) / size - radius;

            Relax(gridUid, grid, seed, baseIdx, size, idx, x + 1, y);
            Relax(gridUid, grid, seed, baseIdx, size, idx, x - 1, y);
            Relax(gridUid, grid, seed, baseIdx, size, idx, x, y + 1);
            Relax(gridUid, grid, seed, baseIdx, size, idx, x, y - 1);
        }
    }

    /// <summary>Relaxes one neighbor tile of a settled node.</summary>
    private void Relax(EntityUid gridUid, MapGridComponent grid, Vector2i seed, int baseIdx, int size, int parentIdx, int nx, int ny)
    {
        var radius = _fieldRadius;
        if (nx < -radius || nx > radius || ny < -radius || ny > radius)
            return;

        var idx = baseIdx + (nx + radius) + size * (ny + radius);
        if (_settled[idx])
            return;

        var weight = TileWeight(gridUid, grid, new Vector2i(seed.X + nx, seed.Y + ny), idx);
        var dist = _dist[parentIdx] + 1f + weight;
        if (dist >= _dist[idx])
            return;

        _dist[idx] = dist;
        _hops[idx] = _hops[parentIdx] + 1;
        _sumW[idx] = _sumW[parentIdx] + weight;
        _past[idx] = weight > 0f ? 0 : _past[parentIdx] + 1;
        Push(idx, dist);
    }

    /// <summary>Returns the blocking occlusion of a tile, cached per root build.</summary>
    private float TileWeight(EntityUid gridUid, MapGridComponent grid, Vector2i tile, int idx)
    {
        var cached = _tileW[idx];
        if (cached >= 0f)
            return cached;

        var weight = ComputeTileWeight(gridUid, grid, tile);
        _tileW[idx] = weight;
        return weight;
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

            if (!TryComp(ent.Value, out PhysicsComponent? phys) || !phys.CanCollide)
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

    /// <summary>Applies smoothed path-based volume to managed audio streams and snapshots values for the debug tools.</summary>
    private void ProcessSounds(EntityUid gridUid, MapGridComponent grid)
    {
        var listener = _audio.GetListenerCoordinates();
        var tileSize = grid.TileSize;
        var maxDist = _fieldRadius * tileSize;

        SweepSmoothVol();
        _candidates.Clear();

        var query = AllEntityQuery<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Global || xform.MapID != listener.MapId || xform.GridUid != gridUid)
                continue;

            var sourcePos = _transform.GetWorldPosition(xform);
            var distance = (sourcePos - listener.Position).Length();
            if (distance > maxDist)
                continue;

            _candidates.Add(new SoundCandidate(uid, comp, xform, sourcePos.X, sourcePos.Y, distance));
        }

        if (_candidates.Count > _maxSources)
        {
            _candidates.Sort(SortByDistance);
            _candidates.RemoveRange(_maxSources, _candidates.Count - _maxSources);
        }

        _debug.Clear();

        foreach (var candidate in _candidates)
        {
            var comp = candidate.Comp;

            if (!TryGetAudio(listener, new Vector2(candidate.SourceX, candidate.SourceY), out var occlusion, out var volDb, out var dbgSumW, out var dbgHops, out var dbgPast))
                continue;

            var target = comp.Params.Volume + volDb;
            var applied = _smoothVol.TryGetValue(candidate.Uid, out var prev)
                ? prev + (target - prev) * _smoothing
                : target;

            _smoothVol[candidate.Uid] = applied;
            comp.Volume = applied;
            _debug[candidate.Uid] = new DebugEntry(dbgSumW, dbgHops, occlusion, applied, volDb, dbgPast, true);
        }
    }

    /// <summary>Removes deleted or compless audio streams from the volume-smoothing set.</summary>
    private void SweepSmoothVol()
    {
        if (_smoothVol.Count == 0)
            return;

        _volSweep.Clear();

        foreach (var uid in _smoothVol.Keys)
        {
            if (!Exists(uid) || !TryComp(uid, out AudioComponent? _))
                _volSweep.Add(uid);
        }

        foreach (var uid in _volSweep)
        {
            _smoothVol.Remove(uid);
        }
    }

    /// <summary>Pushes a node onto the preallocated min-heap.</summary>
    private void Push(int idx, float key)
    {
        var i = _heapCount++;
        _heapIdx[i] = idx;
        _heapKey[i] = key;

        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (_heapKey[parent] <= _heapKey[i])
                break;

            (_heapKey[parent], _heapKey[i]) = (_heapKey[i], _heapKey[parent]);
            (_heapIdx[parent], _heapIdx[i]) = (_heapIdx[i], _heapIdx[parent]);
            i = parent;
        }
    }

    /// <summary>Pops the minimum-key node from the preallocated min-heap.</summary>
    private int PopMin()
    {
        var top = _heapIdx[0];
        _heapCount--;

        if (_heapCount > 0)
        {
            _heapIdx[0] = _heapIdx[_heapCount];
            _heapKey[0] = _heapKey[_heapCount];

            var i = 0;
            while (true)
            {
                var left = 2 * i + 1;
                if (left >= _heapCount)
                    break;

                var child = left;
                var right = left + 1;
                if (right < _heapCount && _heapKey[right] < _heapKey[left])
                    child = right;

                if (_heapKey[i] <= _heapKey[child])
                    break;

                (_heapKey[i], _heapKey[child]) = (_heapKey[child], _heapKey[i]);
                (_heapIdx[i], _heapIdx[child]) = (_heapIdx[child], _heapIdx[i]);
                i = child;
            }
        }

        return top;
    }
}
