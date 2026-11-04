using Content.Server.Station.Systems;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Content.Shared.Tag;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Sets a station grid up for the global star light: a constant <see cref="SunShadowComponent"/> and a marker.
/// All lighting is computed on the client, see Content.Client._Maid.Lighting.StationSunShadowOverlay.
/// </summary>
public sealed class StationSunShadowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(5);

    private TimeSpan _nextRefresh;
    private readonly Dictionary<EntityUid, MapGridComponent> _grids = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _transparent = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _blocking = new();

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
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshDelay;
        RefreshTransparent();
    }

    /// <summary>
    /// Collects the tiles that only hold grilles or solar panels and sends them to clients with the marker, because
    /// a client only knows entities inside its PVS range but sees the tiles of far away parts of the station.
    /// </summary>
    private void RefreshTransparent()
    {
        _grids.Clear();
        _transparent.Clear();
        _blocking.Clear();

        var markers = EntityQueryEnumerator<StationSunMarkerComponent, MapGridComponent>();
        while (markers.MoveNext(out var gridUid, out _, out var gridComp))
        {
            _grids[gridUid] = gridComp;
            _transparent[gridUid] = new HashSet<Vector2i>();
            _blocking[gridUid] = new HashSet<Vector2i>();
        }

        if (_grids.Count == 0)
            return;

        var entities = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (entities.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid is not { } gridUid || !xform.Anchored || !_grids.TryGetValue(gridUid, out var gridComp))
                continue;

            var tile = _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates);

            if (IsTransparent(meta.EntityPrototype?.ID))
            {
                _transparent[gridUid].Add(tile);
            }
            else if (_tag.HasTag(uid, WallTag) ||
                     _tag.HasTag(uid, WindowTag) ||
                     (TryComp(uid, out OccluderComponent? occluder) && occluder.Enabled))
            {
                _blocking[gridUid].Add(tile);
            }
        }

        foreach (var (gridUid, tiles) in _transparent)
        {
            var chunks = new Dictionary<Vector2i, ulong>();
            var blocking = _blocking[gridUid];

            foreach (var tile in tiles)
            {
                // A window or wall on the same tile still casts a shadow.
                if (blocking.Contains(tile))
                    continue;

                var key = new Vector2i(tile.X >> 3, tile.Y >> 3);
                chunks.TryGetValue(key, out var bits);
                chunks[key] = bits | (1UL << ((tile.X & 7) + ((tile.Y & 7) << 3)));
            }

            var marker = Comp<StationSunMarkerComponent>(gridUid);
            if (SameChunks(marker.Transparent, chunks))
                continue;

            marker.Transparent = chunks;
            Dirty(gridUid, marker);
        }
    }

    private static bool SameChunks(Dictionary<Vector2i, ulong> a, Dictionary<Vector2i, ulong> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (key, bits) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != bits)
                return false;
        }

        return true;
    }

    private static bool IsTransparent(string? prototype)
    {
        return prototype != null && (prototype.Contains("Grille") || prototype.Contains("SolarPanel") || prototype.Contains("SolarTracker"));
    }
}
