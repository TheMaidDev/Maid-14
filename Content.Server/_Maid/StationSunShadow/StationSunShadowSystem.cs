using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Station.Systems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
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

    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private static readonly TimeSpan UpdateDelay = TimeSpan.FromSeconds(2);
    private const int ExposureCheckDistance = 6;

    private readonly List<Vector2i> _windowTiles = new();
    private readonly HashSet<Vector2i> _lit = new();

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
            if (_timing.CurTime < state.NextUpdate)
                continue;

            state.NextUpdate = _timing.CurTime + UpdateDelay;

            if (!TryComp(state.Station, out StationSunShadowComponent? sun))
                continue;

            UpdateGrid((uid, gridComp, state), sun);
        }
    }

    private void UpdateGrid(Entity<MapGridComponent, StationSunGridComponent> grid, StationSunShadowComponent sun)
    {
        // Light travels along the shadow direction, snapped to the dominant axis.
        var dir = sun.Direction;
        Vector2i step;
        if (MathF.Abs(dir.X) >= MathF.Abs(dir.Y))
            step = new Vector2i(MathF.Sign(dir.X), 0);
        else
            step = new Vector2i(0, MathF.Sign(dir.Y));

        _lit.Clear();

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

            foreach (var origin in _windowTiles)
            {
                if (!IsExposed(grid, origin, -step))
                    continue;

                var tile = origin;
                for (var i = 0; i <= sun.WindowLightDepth; i++)
                {
                    if (_map.GetTileRef(grid.Owner, grid.Comp1, tile).Tile.IsEmpty)
                        break;

                    // The window tile itself is allowed, anything else solid stops the light.
                    if (i > 0 && IsBlocked(grid, tile))
                        break;

                    _lit.Add(tile);
                    tile += step;
                }
            }
        }

        // Every tile gets roofed except the lit ones; SetRoof only dirties on actual change.
        var roofGrid = (grid.Owner, (MapGridComponent?) grid.Comp1, (RoofComponent?) null);
        foreach (var tileRef in _map.GetAllTiles(grid.Owner, grid.Comp1))
        {
            _roof.SetRoof(roofGrid, tileRef.GridIndices, !_lit.Contains(tileRef.GridIndices));
        }

        grid.Comp2.LitTiles.Clear();
        grid.Comp2.LitTiles.UnionWith(_lit);
    }

    /// <summary>
    /// A window is exposed if looking against the light we reach open space before hitting something solid.
    /// </summary>
    private bool IsExposed(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i origin, Vector2i upstream)
    {
        var tile = origin;
        for (var i = 0; i < ExposureCheckDistance; i++)
        {
            tile += upstream;

            if (_map.GetTileRef(grid.Owner, grid.Comp1, tile).Tile.IsEmpty)
                return true;

            if (IsBlocked(grid, tile))
                return false;
        }

        return false;
    }

    private bool IsBlocked(Entity<MapGridComponent, StationSunGridComponent> grid, Vector2i tile)
    {
        foreach (var ent in _map.GetAnchoredEntities(grid.Owner, grid.Comp1, tile))
        {
            if (!TryComp(ent, out AirtightComponent? airtight) || !airtight.AirBlocked)
                continue;

            if (_tag.HasTag(ent, WindowTag))
                continue;

            return true;
        }

        return false;
    }
}
