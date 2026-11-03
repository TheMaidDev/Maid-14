using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Station.Systems;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.StationSunShadow;

public sealed class StationSunShadowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private static readonly TimeSpan UpdateDelay = TimeSpan.FromSeconds(2);
    private const int ExposureCheckDistance = 6;
    private const float MaxShadowLength = 150f;

    private readonly List<Vector2i> _windowTiles = new();
    private readonly HashSet<Vector2i> _lit = new();
    private readonly HashSet<Vector2i> _gapFill = new();
    private readonly HashSet<Vector2i> _outer = new();
    private readonly Queue<Vector2i> _queue = new();
    private readonly List<Vector2i> _ray = new();
    private readonly HashSet<Vector2i> _solid = new();
    private readonly HashSet<Vector2i> _shadow = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationSunShadowComponent, StationGridAddedEvent>(OnGridAdded);
    }

    private void OnGridAdded(EntityUid uid, StationSunShadowComponent comp, StationGridAddedEvent args)
    {
        var grid = args.GridId;

        var shadow = EnsureComp<SunShadowComponent>(grid);
        shadow.Direction = comp.Direction;
        shadow.Alpha = comp.Alpha;
        Dirty(grid, shadow);

        EnsureComp<StationSunMarkerComponent>(grid);

        var mapUid = Transform(grid).MapUid;
        if (mapUid != null)
        {
            var light = EnsureComp<MapLightComponent>(mapUid.Value);
            light.AmbientLightColor = comp.SunColor;
            Dirty(mapUid.Value, light);
        }

        if (!comp.LightThroughWindows)
            return;

        // Whole-grid roof would make windows pointless, switch to per-tile roof so lit tiles can be punched out.
        RemComp<ImplicitRoofComponent>(grid);
        EnsureComp<RoofComponent>(grid);
        EnsureComp<StationSunGridComponent>(grid).Station = uid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StationSunGridComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var state, out var gridComp))
        {
            if (!TryComp(state.Station, out StationSunShadowComponent? sun) ||
                !TryComp(uid, out SunShadowComponent? shadow))
            {
                continue;
            }

            var now = _timing.CurTime;
            if (now < state.NextMinUpdate)
                continue;

            // The light direction is read from the grid's SunShadow (editable live) and follows the grid rotation.
            var theta = _xform.GetWorldRotation(uid).Theta;
            var changed = MathF.Abs(shadow.Direction.X - state.LastDirection.X) > 0.001f ||
                          MathF.Abs(shadow.Direction.Y - state.LastDirection.Y) > 0.001f ||
                          Math.Abs(theta - state.LastTheta) > 0.001;

            if (!changed && now < state.NextUpdate)
                continue;

            state.NextUpdate = now + UpdateDelay;
            state.NextMinUpdate = now + TimeSpan.FromMilliseconds(250);
            state.LastDirection = shadow.Direction;
            state.LastTheta = theta;

            UpdateGrid((uid, gridComp, state), sun, shadow.Direction, theta);
        }
    }

    private void UpdateGrid(Entity<MapGridComponent, StationSunGridComponent> grid,
        StationSunShadowComponent sun,
        Vector2 worldDir,
        double gridTheta)
    {
        // Light direction is a world-space vector, bring it into the grid's local space.
        var localDir = new Angle(-gridTheta).RotateVec(worldDir);

        // Snapped to the dominant axis, only used to decide the cross-beam axis when filling gaps.
        Vector2i step;
        if (MathF.Abs(localDir.X) >= MathF.Abs(localDir.Y))
            step = new Vector2i(MathF.Sign(localDir.X), 0);
        else
            step = new Vector2i(0, MathF.Sign(localDir.Y));

        _lit.Clear();
        ComputeOuterSpace(grid);

        if (step != Vector2i.Zero)
        {
            _windowTiles.Clear();

            var windows = EntityQueryEnumerator<AirtightComponent, TransformComponent>();
            while (windows.MoveNext(out var winUid, out _, out var xform))
            {
                if (xform.GridUid != grid.Owner || !xform.Anchored)
                    continue;

                if (!_tag.HasTag(winUid, WindowTag))
                    continue;

                _windowTiles.Add(_map.TileIndicesFor(grid.Owner, grid.Comp1, xform.Coordinates));
            }

            var dirN = Vector2.Normalize(localDir);

            foreach (var origin in _windowTiles)
            {
                if (!IsExposed(grid, origin, -dirN))
                    continue;

                SampleTiles(origin, dirN, sun.WindowLightDepth);
                for (var i = 0; i < _ray.Count; i++)
                {
                    var tile = _ray[i];

                    if (IsSpaceTile(grid, tile))
                        break;

                    // The window tile itself is allowed, anything else solid stops the light.
                    if (i > 0 && IsBlocked(grid, tile))
                        break;

                    _lit.Add(tile);
                }
            }
        }

        if (step != Vector2i.Zero && sun.WindowLightGapFill > 0 && _lit.Count > 0)
            FillGaps(grid, step, sun.WindowLightGapFill);

        ComputeShadow(grid, localDir, worldDir.Length());

        // Every tile gets roofed except lit ones and space connected to the outside.
        // Space enclosed inside the station (a hole) is roofed too, so no light comes through it.
        var roofGrid = (grid.Owner, (MapGridComponent?) grid.Comp1, (RoofComponent?) null);
        var size = grid.Comp1.TileSize;
        var aabb = grid.Comp1.LocalAABB;
        var minX = (int) MathF.Floor(aabb.Left / size);
        var maxX = (int) MathF.Ceiling(aabb.Right / size);
        var minY = (int) MathF.Floor(aabb.Bottom / size);
        var maxY = (int) MathF.Ceiling(aabb.Top / size);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                var index = new Vector2i(x, y);
                var tileRef = _map.GetTileRef(grid.Owner, grid.Comp1, index);

                bool roofed;
                if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
                    roofed = !_outer.Contains(index) || _shadow.Contains(index);
                else
                    roofed = !_lit.Contains(index);

                _roof.SetRoof(roofGrid, index, roofed);
            }
        }

        grid.Comp2.LitTiles.Clear();
        grid.Comp2.LitTiles.UnionWith(_lit);
    }

    /// <summary>
    /// Finds space tiles inside the grid's bounds that lie in the shadow of the station mass. Those are roofed
    /// so they stay fully dark even when they are open to space (cavities at the station edges).
    /// The part of the shadow that reaches beyond the grid is drawn by the client overlay.
    /// </summary>
    private void ComputeShadow(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2 localDir, float length)
    {
        _shadow.Clear();
        _solid.Clear();

        length = MathF.Min(length, MaxShadowLength);
        if (length < 0.01f || localDir.LengthSquared() < 0.0001f)
            return;

        var dir = Vector2.Normalize(localDir);
        var perp = new Vector2(-dir.Y, dir.X);

        var size = grid.Comp1.TileSize;
        var aabb = grid.Comp1.LocalAABB;
        var minX = (int) MathF.Floor(aabb.Left / size);
        var maxX = (int) MathF.Ceiling(aabb.Right / size);
        var minY = (int) MathF.Floor(aabb.Bottom / size);
        var maxY = (int) MathF.Ceiling(aabb.Top / size);

        foreach (var tileRef in _map.GetAllTiles(grid.Owner, grid.Comp1))
        {
            if (!_turf.IsSpace(tileRef))
                _solid.Add(tileRef.GridIndices);
        }

        foreach (var solid in _solid)
        {
            if (!IsBoundary(solid))
                continue;

            var center = new Vector2(solid.X + 0.5f, solid.Y + 0.5f);

            // Three parallel rays (centre and both edges) so no tile of the swept strip is skipped.
            for (var k = -1; k <= 1; k++)
            {
                var origin = center + perp * (0.45f * k);

                for (var t = 0f; t <= length; t += 0.5f)
                {
                    var point = origin + dir * t;
                    var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

                    if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                        break;

                    if (!_solid.Contains(tile))
                        _shadow.Add(tile);
                }
            }
        }
    }

    private bool IsBoundary(Vector2i tile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && !_solid.Contains(new Vector2i(tile.X + dx, tile.Y + dy)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lights floor tiles that sit in a narrow gap between two lit tiles across the light direction.
    /// </summary>
    private void FillGaps(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i step, int maxGap)
    {
        var lateral = step.X == 0 ? new Vector2i(1, 0) : new Vector2i(0, 1);
        _gapFill.Clear();

        foreach (var tile in _lit)
        {
            for (var gap = 1; gap <= maxGap; gap++)
            {
                if (!_lit.Contains(tile + lateral * (gap + 1)))
                    continue;

                var fillable = true;
                for (var k = 1; k <= gap; k++)
                {
                    var between = tile + lateral * k;

                    if (_lit.Contains(between) ||
                        IsSpaceTile(grid, between) ||
                        IsBlocked(grid, between))
                    {
                        fillable = false;
                        break;
                    }
                }

                if (!fillable)
                    continue;

                for (var k = 1; k <= gap; k++)
                {
                    _gapFill.Add(tile + lateral * k);
                }
            }
        }

        _lit.UnionWith(_gapFill);
    }

    /// <summary>
    /// A window is exposed if looking against the light we reach open space before hitting something solid.
    /// </summary>
    private bool IsExposed(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i origin, Vector2 upstream)
    {
        SampleTiles(origin, upstream, ExposureCheckDistance);

        for (var i = 1; i < _ray.Count; i++)
        {
            var tile = _ray[i];

            // Space enclosed inside the station (a hole) is not a light source.
            if (IsSpaceTile(grid, tile))
                return _outer.Contains(tile);

            // Looking back through another window means we are inside a room, not facing open space.
            if (IsBlocked(grid, tile, windowsBlock: true))
                return false;
        }

        return false;
    }

    /// <summary>
    /// Fills <see cref="_ray"/> with the distinct tiles crossed by a ray from the centre of <paramref name="origin"/>
    /// along <paramref name="dir"/> (normalized), up to <paramref name="length"/> tiles. The first entry is the origin.
    /// </summary>
    private void SampleTiles(Vector2i origin, Vector2 dir, float length)
    {
        _ray.Clear();
        _ray.Add(origin);

        var center = new Vector2(origin.X + 0.5f, origin.Y + 0.5f);
        for (var t = 0.25f; t <= length; t += 0.25f)
        {
            var point = center + dir * t;
            var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

            if (tile != _ray[_ray.Count - 1])
                _ray.Add(tile);
        }
    }

    /// <summary>
    /// Finds all space-like tiles connected to the space outside the grid. Space enclosed by the station
    /// (holes) is not part of it, so it neither lights tiles nor counts as open sky.
    /// </summary>
    private void ComputeOuterSpace(Entity<MapGridComponent, StationSunGridComponent> grid)
    {
        _outer.Clear();
        _queue.Clear();

        var size = grid.Comp1.TileSize;
        var aabb = grid.Comp1.LocalAABB;
        var margin = ExposureCheckDistance + 1;
        var minX = (int) MathF.Floor(aabb.Left / size) - margin;
        var maxX = (int) MathF.Ceiling(aabb.Right / size) + margin;
        var minY = (int) MathF.Floor(aabb.Bottom / size) - margin;
        var maxY = (int) MathF.Ceiling(aabb.Top / size) + margin;

        var start = new Vector2i(minX, minY);
        _outer.Add(start);
        _queue.Enqueue(start);

        while (_queue.Count > 0)
        {
            var tile = _queue.Dequeue();

            for (var i = 0; i < 4; i++)
            {
                var next = i switch
                {
                    0 => tile + new Vector2i(1, 0),
                    1 => tile + new Vector2i(-1, 0),
                    2 => tile + new Vector2i(0, 1),
                    _ => tile + new Vector2i(0, -1),
                };

                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                    continue;

                if (_outer.Contains(next) || !IsSpaceTile(grid, next))
                    continue;

                _outer.Add(next);
                _queue.Enqueue(next);
            }
        }
    }

    /// <summary>
    /// Empty tiles and space-like tiles such as lattice let light pass and are never roofed.
    /// </summary>
    private bool IsSpaceTile(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(grid.Owner, grid.Comp1, tile);
        return tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef);
    }

    private bool IsBlocked(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i tile, bool windowsBlock = false)
    {
        foreach (var ent in _map.GetAnchoredEntities(grid.Owner, grid.Comp1, tile))
        {
            if (!TryComp(ent, out AirtightComponent? airtight) || !airtight.AirBlocked)
                continue;

            if (!windowsBlock && _tag.HasTag(ent, WindowTag))
                continue;

            return true;
        }

        return false;
    }
}
