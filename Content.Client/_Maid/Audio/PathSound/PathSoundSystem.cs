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
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Replaces the engine's single-raycast occlusion with four weighted Dijkstra tile fields rooted
///     around the local player's position; managed <see cref="AudioComponent"/> streams get path-based
///     occlusion and volume, and the engine's own raycast is disabled for them via
///     <see cref="AudioFlags.NoOcclusion"/>.
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

    /// <summary>Heuristic weight for opaque blockers.</summary>
    private float _weightOpaque;

    /// <summary>Heuristic weight for glass blockers.</summary>
    private float _weightGlass;

    /// <summary>Seconds before the field is rebuilt.</summary>
    private float _ttl;

    /// <summary>Interval between post-passes in seconds.</summary>
    private float _updateInterval;

    /// <summary>Accumulated frame time towards the next post-pass.</summary>
    private float _accum;

    private bool _overlayEnabled;
    private PathSoundOverlay? _overlay;

    private EntityUid? _fieldGrid;
    private Vector2i _baseTile;
    private int _fieldRadius;
    private TimeSpan _fieldTime;

    /// <summary>Path cost per root field tile, indexed root*count + (x+R) + W*(y+R).</summary>
    private float[] _dist = Array.Empty<float>();

    /// <summary>Accumulated blocking weight per root field tile.</summary>
    private float[] _sumW = Array.Empty<float>();

    /// <summary>Path hop count per root field tile.</summary>
    private int[] _hops = Array.Empty<int>();

    /// <summary>Dijkstra settled flag per root field tile.</summary>
    private bool[] _settled = Array.Empty<bool>();

    /// <summary>Cached tile weight per root field tile, -1 while unset.</summary>
    private float[] _tileW = Array.Empty<float>();

    private int[] _heapIdx = Array.Empty<int>();
    private float[] _heapKey = Array.Empty<float>();
    private int _heapCount;

    /// <summary>Whether the engine occlusion override has been registered by this instance.</summary>
    private bool _overrideSubscribed;

    /// <summary>Reused candidate list for the post-pass.</summary>
    private readonly List<SoundCandidate> _candidates = new();

    /// <summary>Last applied per-sound values for the soundpathdebug dump command.</summary>
    private readonly Dictionary<EntityUid, DebugEntry> _debug = new();

    private EntityQuery<SoundPathWeightComponent> _weightQuery;

    /// <summary>One candidate audio stream for the post-pass.</summary>
    private readonly record struct SoundCandidate(EntityUid Uid, AudioComponent Comp, TransformComponent Xform, float SourceX, float SourceY, float Distance);

    /// <summary>Snapshot of one managed sound's computed values for the debug dump.</summary>
    private readonly record struct DebugEntry(float SumWeight, float PathLen, float Delta, float Occlusion, float Volume, bool Written);

    /// <summary>Formats the last post-pass values of managed sounds for the soundpathdebug command.</summary>
    public string DumpDebug()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"managed sounds: {_debug.Count}, radius: {_fieldRadius}, grid: {_fieldGrid}, seed: {_baseTile}, enabled: {_enabled}");

        foreach (var (uid, entry) in _debug)
        {
            sb.AppendLine($"{uid} sumW={entry.SumWeight:0.00} path={entry.PathLen:0.0} delta={entry.Delta:0.0} occl={entry.Occlusion:0.00} vol={entry.Volume:0.0} written={entry.Written}");
        }

        return sb.ToString();
    }

    /// <summary>Distance comparison for candidate sorting.</summary>
    private static readonly Comparison<SoundCandidate> SortByDistance = (a, b) => a.Distance.CompareTo(b.Distance);

    /// <summary>Registers or unregisters the field debug overlay.</summary>
    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set
        {
            if (_overlayEnabled == value)
                return;

            _overlayEnabled = value;
            if (value)
                _overlayManager.AddOverlay(_overlay!);
            else
                _overlayManager.RemoveOverlay(_overlay!);
        }
    }

    /// <summary>Current clamped field radius in tiles.</summary>
    public int Radius => _radius;

    /// <summary>Grid the field is currently built on, if any.</summary>
    public EntityUid? FieldGrid => _fieldGrid;

    /// <summary>Base tile of the field window; seed of root 0.</summary>
    public Vector2i FieldSeed => _baseTile;

    /// <summary>Whether the fields are built and not expired.</summary>
    public bool FieldReady => _fieldGrid != null && (float) (_timing.CurTime - _fieldTime).TotalSeconds < _ttl;

    /// <summary>Returns the blocking weight and hop count for a grid tile in root 0, if covered and reachable.</summary>
    public bool TryGetTile(Vector2i tile, out float sumWeight, out int pathLen)
    {
        if (_fieldGrid != null
            && Math.Abs(tile.X - _baseTile.X) <= _fieldRadius
            && Math.Abs(tile.Y - _baseTile.Y) <= _fieldRadius)
        {
            var size = _fieldRadius * 2 + 1;
            var idx = (tile.X - _baseTile.X + _fieldRadius) + size * (tile.Y - _baseTile.Y + _fieldRadius);
            if (!float.IsPositiveInfinity(_dist[idx]))
            {
                sumWeight = _sumW[idx];
                pathLen = _hops[idx];
                return true;
            }
        }

        sumWeight = 0f;
        pathLen = 0;
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
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightOpaque, v => _weightOpaque = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathWeightGlass, v => _weightGlass = v, true);
        Subs.CVar(_cfg, MaidCVars.SoundPathCacheTtl, v => _ttl = MathF.Max(0f, v), true);
        Subs.CVar(_cfg, MaidCVars.SoundPathUpdateRate, v => _updateInterval = v > 0 ? 1f / v : float.MaxValue, true);

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

        if (_overlayEnabled)
        {
            _overlayManager.RemoveOverlay(_overlay!);
            _overlayEnabled = false;
        }

        if (_overrideSubscribed)
        {
            _audio.GetOcclusionOverride -= OnGetOcclusionOverride;
            _overrideSubscribed = false;
        }

        _fieldGrid = null;
    }

    /// <summary>Rebuilds the fields when needed and runs the managed-sound post-pass at the configured rate.</summary>
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

    /// <summary>Interpolates the path values from the fields for a sound at the given world position.</summary>
    private bool TryGetPath(MapCoordinates listener, Vector2 sourcePos, out float sumW, out float hops)
    {
        sumW = 0f;
        hops = 0f;

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
        var recvAx = recvFx - recvBx;
        var recvAy = recvFy - recvBy;

        var srcLocal = Vector2.Transform(sourcePos, inv);
        var srcFx = srcLocal.X / tileSize;
        var srcFy = srcLocal.Y / tileSize;
        var srcBx = (int) MathF.Floor(srcFx);
        var srcBy = (int) MathF.Floor(srcFy);
        var sax = srcFx - srcBx;
        var say = srcFy - srcBy;

        var radius = _fieldRadius;
        var size = radius * 2 + 1;
        var baseTile = _baseTile;

        var rootSumW = 0f;
        var rootSumH = 0f;
        var rootWeightSum = 0f;

        for (var root = 0; root < RootCount; root++)
        {
            var rootDx = root & 1;
            var rootDy = root >> 1;
            var rootWeight = (rootDx == 0 ? 1f - recvAx : recvAx) * (rootDy == 0 ? 1f - recvAy : recvAy);

            var cornerSumW = 0f;
            var cornerSumH = 0f;
            var cornerWeightSum = 0f;

            for (var corner = 0; corner < RootCount; corner++)
            {
                var cdx = corner & 1;
                var cdy = corner >> 1;
                var cornerX = srcBx + cdx;
                var cornerY = srcBy + cdy;
                var offsetX = cornerX - baseTile.X - rootDx;
                var offsetY = cornerY - baseTile.Y - rootDy;

                if (offsetX < -radius || offsetX > radius || offsetY < -radius || offsetY > radius)
                    continue;

                if (!_map.TryGetTile(grid, new Vector2i(cornerX, cornerY), out _))
                    continue;

                var weight = (cdx == 0 ? 1f - sax : sax) * (cdy == 0 ? 1f - say : say);
                var idx = root * size * size + (offsetX + radius) + size * (offsetY + radius);

                cornerSumW += weight * _sumW[idx];
                cornerSumH += weight * _hops[idx];
                cornerWeightSum += weight;
            }

            if (cornerWeightSum <= 0f)
                continue;

            rootSumW += rootWeight * (cornerSumW / cornerWeightSum);
            rootSumH += rootWeight * (cornerSumH / cornerWeightSum);
            rootWeightSum += rootWeight;
        }

        if (rootWeightSum <= 0f)
            return false;

        sumW = rootSumW / rootWeightSum;
        hops = rootSumH / rootWeightSum;
        return true;
    }

    /// <summary>Engine occlusion override: field-based value for sounds inside the field, engine raycast otherwise.</summary>
    private float OnGetOcclusionOverride(MapCoordinates listener, Vector2 delta, float distance, EntityUid? ignoredEnt)
    {
        if (distance <= 0.1f)
            return 0f;

        if (TryGetPath(listener, listener.Position + delta, out var sumW, out _))
            return _occlusionPerWeight * sumW;

        var rayLength = MathF.Min(distance, _cfg.GetCVar(CVars.AudioRaycastLength));
        var ray = new CollisionRay(listener.Position, delta / distance, _sharedAudio.OcclusionCollisionMask);
        return _physics.IntersectRayPenetration(listener.MapId, ray, rayLength, ignoredEnt);
    }

    /// <summary>Allocates the per-root field and shared heap arrays for the current radius.</summary>
    private void AllocateField(int radius)
    {
        var size = radius * 2 + 1;
        var count = size * size;

        _dist = new float[count * RootCount];
        _sumW = new float[count * RootCount];
        _hops = new int[count * RootCount];
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
        Push(idx, dist);
    }

    /// <summary>Returns the blocking weight of a tile, cached per root build.</summary>
    private float TileWeight(EntityUid gridUid, MapGridComponent grid, Vector2i tile, int idx)
    {
        var cached = _tileW[idx];
        if (cached >= 0f)
            return cached;

        var weight = ComputeTileWeight(gridUid, grid, tile);
        _tileW[idx] = weight;
        return weight;
    }

    /// <summary>Computes the blocking weight of a tile from its anchored entities' hard impassable fixtures.</summary>
    private float ComputeTileWeight(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTile(grid, tile, out var tileData) || tileData.IsEmpty)
            return 0f;

        var best = 0f;
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (!TryComp(ent.Value, out FixturesComponent? fixtures))
                continue;

            var layer = 0;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionLayer & ImpassableMask) == 0)
                    continue;

                layer |= fixture.CollisionLayer;
            }

            if (layer == 0)
                continue;

            float weight;
            if (_weightQuery.TryGetComponent(ent.Value, out var weightComp))
                weight = weightComp.Weight;
            else if ((layer & OpaqueMask) != 0)
                weight = _weightOpaque;
            else if ((layer & GlassMask) != 0)
                weight = _weightGlass;
            else
                weight = _weightOpaque;

            if (weight > best)
                best = weight;
        }

        return best;
    }

    /// <summary>Applies path-based occlusion and volume to managed audio streams.</summary>
    private void ProcessSounds(EntityUid gridUid, MapGridComponent grid)
    {
        var listener = _audio.GetListenerCoordinates();
        var tileSize = grid.TileSize;
        var maxDist = _fieldRadius * tileSize;

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

            if (!TryGetPath(listener, new Vector2(candidate.SourceX, candidate.SourceY), out var interpSumW, out var interpHops))
                continue;

            var delta = interpHops - (MathF.Abs(candidate.SourceX - listener.Position.X) + MathF.Abs(candidate.SourceY - listener.Position.Y)) / tileSize;
            if (interpSumW < 0.01f && delta < 0.5f)
            {
                _debug[candidate.Uid] = new DebugEntry(interpSumW, interpHops, delta, 0f, comp.Params.Volume, false);
                continue;
            }

            comp.Occlusion = _occlusionPerWeight * interpSumW;
            comp.Volume = comp.Params.Volume - _lossDbPerTile * MathF.Max(0f, delta);
            _debug[candidate.Uid] = new DebugEntry(interpSumW, interpHops, delta, comp.Occlusion, comp.Volume, true);
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
