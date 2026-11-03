using System.Numerics;
using Content.Client.Light;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Casts the shadow of the whole station silhouette along the sun direction onto space tiles.
/// Drawn into the ambient light target next to <see cref="RoofOverlay"/>, so it removes sunlight but not lamp light.
/// </summary>
public sealed class StationSunShadowOverlay : Overlay
{
    private const float MaxShadowLength = 150f;
    private const float SampleStep = 0.5f;
    private const int MaxOtherGridTiles = 4000;
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1);

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;
    private readonly TurfSystem _turf;

    private readonly Dictionary<EntityUid, GridShadow> _cache = new();
    private readonly List<Vector2> _sources = new();
    private readonly List<(EntityUid Uid, MapGridComponent Grid, GridShadow Shadow, Color Color)> _draw = new();

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
            _draw.Add((uid, grid, shadow, Color.Black.WithAlpha(alpha)));
        }

        if (_draw.Count == 0)
            return;

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

                foreach (var (uid, grid, shadow, color) in _draw)
                {
                    var gridMatrix = _xform.GetWorldMatrix(uid);
                    worldHandle.SetTransform(Matrix3x2.Multiply(gridMatrix, invMatrix));

                    var size = grid.TileSize;
                    foreach (var tile in shadow.Tiles)
                    {
                        var center = Vector2.Transform(new Vector2((tile.X + 0.5f) * size, (tile.Y + 0.5f) * size), gridMatrix);
                        if (!bounds.Contains(center))
                            continue;

                        worldHandle.DrawRect(new Box2(tile.X * size, tile.Y * size, (tile.X + 1) * size, (tile.Y + 1) * size), color);
                    }
                }
            }, null);

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    /// Recomputes which space tiles lie in the station's shadow. Throttled, but reacts at once to a changed
    /// light direction or grid rotation.
    /// </summary>
    private void Refresh(EntityUid uid, MapGridComponent grid, Vector2 worldDir, GridShadow shadow)
    {
        var theta = _xform.GetWorldRotation(uid).Theta;
        var now = _timing.CurTime;

        var changed = Vector2.DistanceSquared(worldDir, shadow.Direction) > 0.000001f ||
                      Math.Abs(theta - shadow.Theta) > 0.001;

        if (!changed && now < shadow.NextRefresh)
            return;

        shadow.NextRefresh = now + RefreshDelay;
        shadow.Direction = worldDir;
        shadow.Theta = theta;
        shadow.Tiles.Clear();
        shadow.Solid.Clear();

        var length = MathF.Min(worldDir.Length(), MaxShadowLength);
        if (length < 0.01f)
            return;

        // World-space light direction brought into the grid's local space.
        var localDir = new Angle(-theta).RotateVec(Vector2.Normalize(worldDir));
        var perp = new Vector2(-localDir.Y, localDir.X);

        // The whole station mass blocks the light, everything that is not space-like.
        foreach (var tileRef in _mapSystem.GetAllTiles(uid, grid))
        {
            if (!_turf.IsSpace(tileRef))
                shadow.Solid.Add(tileRef.GridIndices);
        }

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

        DebugInfo = $"Station shadow: length={length:0.0}, solid tiles={shadow.Solid.Count}, " +
                    $"ray sources={ownSources}+{_sources.Count - ownSources} (other grids), shadow tiles={shadow.Tiles.Count}";
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
        public readonly HashSet<Vector2i> Tiles = new();
        public Vector2 Direction;
        public double Theta;
        public TimeSpan NextRefresh;
    }
}
