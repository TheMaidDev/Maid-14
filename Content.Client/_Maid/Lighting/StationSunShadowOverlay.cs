using System.Linq;
using System.Numerics;
using Content.Client.Light;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Client-side star light for station grids: darkens the station interior except for tiles reached by sunlight
/// through exposed windows, and casts the shadow of the whole station silhouette along the sun direction onto space.
/// Drawn into the ambient light target next to <see cref="RoofOverlay"/>, so it removes sunlight but not lamp light.
/// Everything is derived from data the client already has (tiles, occluders, window tags, grid rotation, SunShadow).
/// </summary>
public sealed class StationSunShadowOverlay : Overlay
{
    private const float MaxShadowLength = 150f;
    private const float SampleStep = 0.5f;
    private const int MaxOtherGridTiles = 4000;
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinDirectionalDelay = TimeSpan.FromMilliseconds(16);
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>
    /// How many tiles behind a window the light reaches into the station.
    /// </summary>
    private const int WindowLightDepth = 3;

    /// <summary>
    /// Looking against the light from a window, open space must be found within this many tiles ...
    /// </summary>
    private const float NearRange = 6f;

    /// <summary>
    /// ... and nothing solid may block the way for this many tiles after that, otherwise the window sits in a cavity.
    /// </summary>
    private const float FarRange = 32f;

    /// <summary>
    /// A window's beam is split into this many parallel rays, each one tested for exposure on its own,
    /// so a tile is lit by a fraction and the light moves smoothly instead of tile by tile.
    /// </summary>
    private const int BeamRays = 24;

    private const float BeamStep = 0.125f;

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;
    private readonly TurfSystem _turf;
    private readonly TagSystem _tag;

    private readonly Dictionary<EntityUid, GridShadow> _cache = new();
    private readonly List<Vector2i> _ray = new();
    private readonly List<Vector2> _sources = new();
    private readonly List<(EntityUid Uid, MapGridComponent Grid, GridShadow Shadow, float Alpha)> _draw = new();

    /// <summary>
    /// Summary of the last shadow computation, shown by <c>sunlightdebug</c>.
    /// </summary>
    public static string DebugInfo = string.Empty;

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    public StationSunShadowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _turf = _entManager.System<TurfSystem>();
        _tag = _entManager.System<TagSystem>();

        ZIndex = RoofOverlay.ContentZIndex;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null || !_entManager.HasComponent<MapLightComponent>(args.MapUid))
            return;

        _draw.Clear();

        var query = _entManager.EntityQueryEnumerator<StationSunMarkerComponent, MapGridComponent, SunShadowComponent>();
        while (query.MoveNext(out var uid, out _, out var grid, out var sun))
        {
            if (_entManager.GetComponent<TransformComponent>(uid).MapID != args.MapId)
                continue;

            var alpha = Math.Clamp(sun.Alpha, 0f, 1f);
            if (alpha <= 0f || sun.Direction == Vector2.Zero)
                continue;

            if (!_cache.TryGetValue(uid, out var shadow))
            {
                shadow = new GridShadow();
                _cache[uid] = shadow;
            }

            Refresh(uid, grid, sun.Direction, shadow);
            _draw.Add((uid, grid, shadow, alpha));
        }

        if (_draw.Count == 0)
            return;

        // Shaded tiles are drawn opaque in a precomputed colour (ambient * (1 - strength)) rather than as translucent
        // black, which is what the roof does too and does not depend on the target's alpha blending.
        var ambient = _entManager.GetComponent<MapLightComponent>(args.MapUid).AmbientLightColor;

        var viewport = args.Viewport;
        var eye = viewport.Eye;
        var worldHandle = args.WorldHandle;
        var lightOverlay = _overlay.GetOverlay<BeforeLightTargetOverlay>();
        var bounds = lightOverlay.EnlargedBounds.CalcBoundingBox();
        var target = lightOverlay.EnlargedLightTarget;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2) viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);

        worldHandle.RenderInRenderTarget(target,
            () =>
            {
                var invMatrix = target.GetWorldToLocalMatrix(eye, scale);

                foreach (var (uid, grid, shadow, alpha) in _draw)
                {
                    var gridMatrix = _xform.GetWorldMatrix(uid);
                    worldHandle.SetTransform(Matrix3x2.Multiply(gridMatrix, invMatrix));

                    var size = grid.TileSize;

                    // Station interior is dark unless sunlight reaches the tile through a window.
                    foreach (var tile in shadow.Solid)
                    {
                        var dark = 1f;
                        if (shadow.Lit.TryGetValue(tile, out var coverage))
                            dark = 1f - coverage;

                        if (dark < 0.01f)
                            continue;

                        var center = Vector2.Transform(new Vector2((tile.X + 0.5f) * size, (tile.Y + 0.5f) * size), gridMatrix);
                        if (!bounds.Contains(center))
                            continue;

                        worldHandle.DrawRect(new Box2(tile.X * size, tile.Y * size, (tile.X + 1) * size, (tile.Y + 1) * size),
                            Shade(ambient, dark));
                    }

                    // The cast shadow fades with Alpha, but space inside the station's own bounds (holes, cavities)
                    // never gets light, so it stays fully dark.
                    var shadowColor = Shade(ambient, alpha);
                    var cavityColor = Shade(ambient, 1f);
                    var localBounds = grid.LocalAABB;
                    foreach (var tile in shadow.Tiles)
                    {
                        var localCenter = new Vector2((tile.X + 0.5f) * size, (tile.Y + 0.5f) * size);
                        var center = Vector2.Transform(localCenter, gridMatrix);
                        if (!bounds.Contains(center))
                            continue;

                        worldHandle.DrawRect(new Box2(tile.X * size, tile.Y * size, (tile.X + 1) * size, (tile.Y + 1) * size),
                            localBounds.Contains(localCenter) ? cavityColor : shadowColor);
                    }
                }
            }, null);

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private static Color Shade(Color ambient, float strength)
    {
        var f = 1f - Math.Clamp(strength, 0f, 1f);
        return new Color(ambient.R * f, ambient.G * f, ambient.B * f, 1f);
    }

    /// <summary>
    /// The grid-dependent data (tiles, blockers, windows) is rescanned about once a second. Everything that depends on
    /// the light direction is recomputed at once when the direction or the grid rotation changes (rate limited).
    /// </summary>
    private void Refresh(EntityUid uid, MapGridComponent grid, Vector2 worldDir, GridShadow shadow)
    {
        var theta = _xform.GetWorldRotation(uid).Theta;
        var now = _timing.CurTime;

        var rescan = now >= shadow.NextRescan;
        var changed = Vector2.DistanceSquared(worldDir, shadow.Direction) > 0.000001f ||
                      Math.Abs(theta - shadow.Theta) > 0.001;

        if (!rescan && (!changed || now < shadow.NextDirectional))
            return;

        if (rescan)
        {
            shadow.NextRescan = now + RefreshDelay;
            Rescan(uid, grid, shadow);
        }

        shadow.NextDirectional = now + MinDirectionalDelay;
        shadow.Direction = worldDir;
        shadow.Theta = theta;
        shadow.Tiles.Clear();
        shadow.Lit.Clear();

        var length = MathF.Min(worldDir.Length(), MaxShadowLength);
        if (length < 0.01f)
            return;

        // World-space light direction brought into the grid's local space.
        var localDir = new Angle(-theta).RotateVec(Vector2.Normalize(worldDir));
        var perp = new Vector2(-localDir.Y, localDir.X);

        var exposed = ComputeLit(shadow, localDir);

        // Points where shadow rays start, in the grid's local tile space.
        _sources.Clear();

        // Only the silhouette matters: a ray from an inner tile leaves through a boundary tile anyway.
        foreach (var solid in shadow.Solid)
        {
            if (IsBoundary(shadow.Solid, solid))
                _sources.Add(new Vector2(solid.X + 0.5f, solid.Y + 0.5f));
        }

        var ownSources = _sources.Count;

        // Everything else on the map blocks the sun as well (docked shuttles, other parts of the station).
        var mapId = _entManager.GetComponent<TransformComponent>(uid).MapID;
        var invMatrix = _xform.GetInvWorldMatrix(uid);
        var size = grid.TileSize;
        var others = _entManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (others.MoveNext(out var otherUid, out var otherGrid, out var otherXform))
        {
            if (otherUid == uid || otherXform.MapID != mapId || _entManager.HasComponent<MapComponent>(otherUid))
                continue;

            var otherMatrix = _xform.GetWorldMatrix(otherUid);
            var toLocal = Matrix3x2.Multiply(otherMatrix, invMatrix);
            var otherSize = otherGrid.TileSize;
            var count = 0;

            foreach (var tileRef in _mapSystem.GetAllTiles(otherUid, otherGrid))
            {
                if (_turf.IsSpace(tileRef) || count++ > MaxOtherGridTiles)
                    continue;

                var center = new Vector2((tileRef.GridIndices.X + 0.5f) * otherSize, (tileRef.GridIndices.Y + 0.5f) * otherSize);
                _sources.Add(Vector2.Transform(center, toLocal) / size);
            }
        }

        // Three parallel rays per source (centre and both edges) so no tile in the swept strip is skipped.
        for (var n = 0; n < _sources.Count; n++)
        {
            var source = _sources[n];

            for (var k = -1; k <= 1; k++)
            {
                var origin = source + perp * (0.45f * k);

                for (var t = 0f; t <= length; t += SampleStep)
                {
                    var point = origin + localDir * t;
                    var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

                    if (!shadow.Solid.Contains(tile))
                        shadow.Tiles.Add(tile);
                }
            }
        }

        DebugInfo = $"Station sun: shadow length={length:0.0}, solid tiles={shadow.Solid.Count}, " +
                    $"ray sources={ownSources}+{_sources.Count - ownSources} (other grids), shadow tiles={shadow.Tiles.Count}, " +
                    $"windows={shadow.Windows.Count} (exposed {exposed}), lit tiles={shadow.Lit.Count}";
    }

    /// <summary>
    /// Collects the solid (non-space) tiles, light blockers and windows of the grid.
    /// </summary>
    private void Rescan(EntityUid uid, MapGridComponent grid, GridShadow shadow)
    {
        shadow.Solid.Clear();
        shadow.Blocked.Clear();
        shadow.Windows.Clear();
        shadow.WindowSet.Clear();

        foreach (var tileRef in _mapSystem.GetAllTiles(uid, grid))
        {
            if (!_turf.IsSpace(tileRef))
                shadow.Solid.Add(tileRef.GridIndices);
        }

        // Walls and closed doors have an enabled occluder.
        var occluders = _entManager.EntityQueryEnumerator<OccluderComponent, TransformComponent>();
        while (occluders.MoveNext(out _, out var occluder, out var xform))
        {
            if (!occluder.Enabled || xform.GridUid != uid || !xform.Anchored)
                continue;

            shadow.Blocked.Add(_mapSystem.TileIndicesFor(uid, grid, xform.Coordinates));
        }

        var tagged = _entManager.EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (tagged.MoveNext(out var tagUid, out _, out var xform))
        {
            if (xform.GridUid != uid || !xform.Anchored)
                continue;

            var tile = _mapSystem.TileIndicesFor(uid, grid, xform.Coordinates);

            if (_tag.HasTag(tagUid, WallTag))
            {
                shadow.Blocked.Add(tile);
            }
            else if (_tag.HasTag(tagUid, WindowTag) && shadow.WindowSet.Add(tile))
            {
                shadow.Windows.Add(tile);
            }
        }

        // Light passes through windows, they only matter as a start of a beam.
        shadow.Blocked.ExceptWith(shadow.WindowSet);

        // Grilles let light through: a tile that has only a grille on it counts as open space, not as station mass.
        var metas = _entManager.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (metas.MoveNext(out var meta, out var xform))
        {
            if (xform.GridUid != uid || !xform.Anchored || meta.EntityPrototype?.ID.Contains("Grille") != true)
                continue;

            var tile = _mapSystem.TileIndicesFor(uid, grid, xform.Coordinates);
            if (!shadow.Blocked.Contains(tile) && !shadow.WindowSet.Contains(tile))
                shadow.Solid.Remove(tile);
        }
    }

    /// <summary>
    /// Lights the tiles behind every window that faces open sky. Each window's beam is made of several parallel rays,
    /// every tile receives the share of the beam that crosses it. Returns the number of exposed windows.
    /// </summary>
    private int ComputeLit(GridShadow shadow, Vector2 dir)
    {
        var exposed = 0;
        var perp = new Vector2(-dir.Y, dir.X);
        var weight = BeamStep / BeamRays;

        foreach (var origin in shadow.Windows)
        {
            var center = new Vector2(origin.X + 0.5f, origin.Y + 0.5f);
            var any = false;

            for (var k = 0; k < BeamRays; k++)
            {
                var start = center + perp * ((k + 0.5f) / BeamRays - 0.5f);

                if (!IsExposed(shadow, origin, start, -dir))
                    continue;

                any = true;

                // From the window's outer face to WindowLightDepth tiles behind it.
                for (var s = -0.5f + BeamStep / 2f; s < WindowLightDepth + 0.5f; s += BeamStep)
                {
                    var point = start + dir * s;
                    var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

                    if (tile != origin)
                    {
                        // Outside the window tile before the window is reached (diagonal beams).
                        if (s < 0f)
                            continue;

                        // Anything solid or open space stops the light.
                        if (!shadow.Solid.Contains(tile) || shadow.Blocked.Contains(tile))
                            break;
                    }

                    shadow.Lit[tile] = shadow.Lit.GetValueOrDefault(tile) + weight;
                }
            }

            if (any)
                exposed++;
        }

        foreach (var tile in shadow.Lit.Keys.ToArray())
        {
            if (shadow.Lit[tile] > 1f)
                shadow.Lit[tile] = 1f;
        }

        return exposed;
    }

    /// <summary>
    /// A beam ray is exposed if, looking against the light, we reach open space soon and nothing solid is in the way
    /// beyond it. Space tiles enclosed by the station (holes, cavities) fail the second check.
    /// </summary>
    private static bool IsExposed(GridShadow shadow, Vector2i origin, Vector2 start, Vector2 upstream)
    {
        var reachedSpace = false;

        for (var t = 0.5f; t <= NearRange + FarRange; t += 0.5f)
        {
            var point = start + upstream * t;
            var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));

            if (tile == origin)
                continue;

            if (!reachedSpace)
            {
                // Looking back through another window or a wall means we are inside a room.
                if (shadow.WindowSet.Contains(tile) || shadow.Blocked.Contains(tile))
                    return false;

                if (!shadow.Solid.Contains(tile))
                    reachedSpace = true;
                else if (t > NearRange)
                    return false;

                continue;
            }

            if (shadow.Solid.Contains(tile) || shadow.Blocked.Contains(tile))
                return false;
        }

        return reachedSpace;
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
    /// How much of the tile (0..1) is currently reached by sunlight through a window.
    /// </summary>
    public float GetLight(EntityUid grid, Vector2i tile)
    {
        return _cache.TryGetValue(grid, out var shadow) ? shadow.Lit.GetValueOrDefault(tile) : 0f;
    }

    public IReadOnlyDictionary<Vector2i, float>? GetLitTiles(EntityUid grid)
    {
        return _cache.TryGetValue(grid, out var shadow) ? shadow.Lit : null;
    }

    private static bool IsBoundary(HashSet<Vector2i> solid, Vector2i tile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && !solid.Contains(new Vector2i(tile.X + dx, tile.Y + dy)))
                    return true;
            }
        }

        return false;
    }

    private sealed class GridShadow
    {
        public readonly HashSet<Vector2i> Solid = new();
        public readonly HashSet<Vector2i> Blocked = new();
        public readonly HashSet<Vector2i> WindowSet = new();
        public readonly List<Vector2i> Windows = new();
        public readonly Dictionary<Vector2i, float> Lit = new();
        public readonly HashSet<Vector2i> Tiles = new();
        public Vector2 Direction;
        public double Theta;
        public TimeSpan NextRescan;
        public TimeSpan NextDirectional;
    }
}
