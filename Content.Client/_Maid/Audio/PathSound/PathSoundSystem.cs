using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using System.Threading;
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

    /// <summary>Minimum field radius in tiles.</summary>
    private const int MinRadius = 4;

    /// <summary>Maximum field radius in tiles.</summary>
    private const int MaxRadius = 32;

    /// <summary>Number of Dijkstra fields rooted around the receiver's tile corners.</summary>
    private const int RootCount = 4;

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
    public readonly record struct DebugEntry(float SumWeight, float PathLen, float Occlusion, float Smoothed, float Past);

    /// <summary>Formats the last filter values per sound for the soundpathdebug command.</summary>
    public string DumpDebug()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"smoothed sounds: {_debug.Count}, radius: {_fieldRadius}, grid: {_fieldGrid}, seed: {_baseTile}, enabled: {_enabled}, smooth: {_smoothing:0.00}");

        foreach (var (uid, entry) in _debug)
        {
            sb.AppendLine($"{uid} occlSum={entry.SumWeight:0.00} path={entry.PathLen:0.0} past={entry.Past:0} occl={entry.Occlusion:0.00} filter={entry.Smoothed:0.00}");
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

    /// <summary>Last filter values per sound for the overlay markers.</summary>
    public IReadOnlyDictionary<EntityUid, DebugEntry> DebugEntries => _debug;

    /// <summary>Test and debug read of the model: raw path occlusion and the folded filter value for a world position, without smoothing.</summary>
    public bool TryGetModelOcclusion(MapCoordinates listener, Vector2 sourcePos, out float occl, out float pathLen, out float past, out float filterOccl)
    {
        filterOccl = 0f;

        if (!TryGetAudio(listener, sourcePos, out occl, out _, out pathLen, out past)
            && !TryGetAudio(listener, sourcePos, out occl, out _, out pathLen, out past, true))
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
    }

    /// <summary>Rebuilds the fields the moment a door changes state: predicted door sounds spawn in the same tick and the engine starts them before the next frame update.</summary>
    private void OnBlockerChanged(DoorStateChangedEvent args)
    {
        _doorDirty = true;
        RebuildNow();
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

    private void OnBlockerChanged(ref AnchorStateChangedEvent args) => _dirty = true;

    private void OnBlockerChanged(ref TileChangedEvent args) => _dirty = true;

    private void OnBlockerChanged(ref CollisionChangeEvent args) => _dirty = true;

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

        if (_fieldGrid != gridUid
            || _baseTile != baseTile
            || _fieldRadius != _radius
            || _doorDirty
            || _dirty && (float) (_timing.CurTime - _fieldTime).TotalSeconds > 0.05f
            || (float) (_timing.CurTime - _fieldTime).TotalSeconds > _ttl)
        {
            RebuildField(gridUid, grid, baseTile);
            _dirty = false;
            _doorDirty = false;
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
    private bool TryGetAudio(MapCoordinates listener, Vector2 sourcePos, out float occlusion, out float dbgSumW, out float dbgHops, out float dbgPast, bool clampToField = false)
    {
        occlusion = 0f;
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
            if (float.IsPositiveInfinity(_dist[idx]))
                continue;

            // A blocker belongs to the path a sound crosses, not to the tile it is born on: the source tile's
            // own weight never muffles that sound, while every blocker the route crosses still does.
            var selfW = srcTile.X == srcBx && srcTile.Y == srcBy
                ? ComputeTileWeight(gridUid, grid, srcTile)
                : 0f;
            var occlR = MathF.Max(0f, _sumW[idx] - selfW);
            var pastR = _sumW[idx] > 0f ? _past[idx] : 0;
            occlR = MathF.Max(0f, occlR - _occlDecay * pastR);

            totalWeight += rootWeight;
            cutoffSum += rootWeight * MathF.Exp(-occlR);
            sumWSum += rootWeight * occlR;
            hopsSum += rootWeight * _hops[idx];
            pastSum += rootWeight * pastR;
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

        if (!crossGrid && TryGetAudio(listener, sourcePos, out occl, out sumW, out len, out past))
        {
            modelled = true;
        }
        else
        {
            // Beyond the field: keep the occlusion accumulated to the field edge along the ray and raycast only the rest.
            var edgeDist = _fieldRadius;
            if (_fieldGrid is { } edgeGrid && TryComp(edgeGrid, out MapGridComponent? edgeGridComp))
                edgeDist = _fieldRadius * edgeGridComp.TileSize;

            if (!crossGrid && distance > edgeDist && TryGetAudio(listener, sourcePos, out occl, out sumW, out len, out past, true))
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

        _debug[entity] = new DebugEntry(sumW, len, occl, smoothed, past);
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
