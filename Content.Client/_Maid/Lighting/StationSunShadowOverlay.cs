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
    /// <summary>
    /// The shadow is stored per strip across the light direction (this wide, in tiles): for each strip the most
    /// upstream solid tile is remembered and everything downstream of it is in shadow, however far.
    /// </summary>
    private const float ShadowBinWidth = 0.25f;

    /// <summary>
    /// Half width of the strip one solid tile shades, a bit over half a tile so diagonal neighbours overlap.
    /// </summary>
    private const float ShadowHalfWidth = 0.55f;

    /// <summary>
    /// Outside the station's own bounds the shadow keeps full strength this far behind the last blocker ...
    /// </summary>
    private const float ShadowFullDistance = 60f;

    /// <summary>
    /// ... then fades linearly and is gone at this distance.
    /// </summary>
    private const float ShadowEndDistance = 100f;
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
    /// Further away the shadow strips decide, so a far away blocker shades the window as well.
    /// </summary>
    private const float FarRange = 10f;

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
    private int[] _cursor = Array.Empty<int>();
    private bool[] _floodOutside = Array.Empty<bool>();
    private int[] _floodQueue = Array.Empty<int>();
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
                    var localBounds = grid.LocalAABB;
                    // Visible part of the grid in tile coordinates.
                    var invGrid = _xform.GetInvWorldMatrix(uid);
                    var c0 = Vector2.Transform(bounds.BottomLeft, invGrid);
                    var c1 = Vector2.Transform(bounds.BottomRight, invGrid);
                    var c2 = Vector2.Transform(bounds.TopRight, invGrid);
                    var c3 = Vector2.Transform(bounds.TopLeft, invGrid);
                    var minX = (int) MathF.Floor(MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X)) / size);
                    var maxX = (int) MathF.Ceiling(MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X)) / size);
                    var minY = (int) MathF.Floor(MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y)) / size);
                    var maxY = (int) MathF.Ceiling(MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y)) / size);

                    if ((long) (maxX - minX) * (maxY - minY) > 400000)
                        continue;

                    for (var x = minX; x <= maxX; x++)
                    {
                        for (var y = minY; y <= maxY; y++)
                        {
                            var tile = new Vector2i(x, y);
                            if (shadow.Solid.Contains(tile))
                                continue;

                            var localCenter = new Vector2((x + 0.5f) * size, (y + 0.5f) * size);
                            var inside = localBounds.Contains(localCenter);
                            var strength = ShadowStrength(shadow, tile);
                            if (strength < 0.01f)
                                continue;

                            worldHandle.DrawRect(new Box2(x * size, y * size, (x + 1) * size, (y + 1) * size),
                                Shade(ambient, inside ? strength : alpha * strength));
                        }
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
        shadow.Lit.Clear();

        if (worldDir.Length() < 0.01f)
        {
            shadow.BinStart = Array.Empty<int>();
            return;
        }

        // World-space light direction brought into the grid's local space.
        var localDir = new Angle(-theta).RotateVec(Vector2.Normalize(worldDir));
        var perp = new Vector2(-localDir.Y, localDir.X);
        shadow.Dir = localDir;
        shadow.Perp = perp;

        // Points that block the sun, in the grid's local tile space: the grid's own solid tiles ...
        _sources.Clear();
        foreach (var solid in shadow.Solid)
        {
            _sources.Add(new Vector2(solid.X + 0.5f, solid.Y + 0.5f));
        }

        var ownSources = _sources.Count;

        // ... and everything else on the map (docked shuttles, other parts of the station).
        var mapId = _entManager.GetComponent<TransformComponent>(uid).MapID;
        var invMatrix = _xform.GetInvWorldMatrix(uid);
        var size = grid.TileSize;
        var others = _entManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (others.MoveNext(out var otherUid, out var otherGrid, out var otherXform))
        {
            if (otherUid == uid || otherXform.MapID != mapId || _entManager.HasComponent<MapComponent>(otherUid))
                continue;

            var toLocal = Matrix3x2.Multiply(_xform.GetWorldMatrix(otherUid), invMatrix);
            var otherSize = otherGrid.TileSize;

            foreach (var tileRef in _mapSystem.GetAllTiles(otherUid, otherGrid))
            {
                if (_turf.IsSpace(tileRef))
                    continue;

                var center = new Vector2((tileRef.GridIndices.X + 0.5f) * otherSize, (tileRef.GridIndices.Y + 0.5f) * otherSize);
                _sources.Add(Vector2.Transform(center, toLocal) / size);
            }
        }

        // For every strip across the light remember the most upstream blocker.
        var qMin = float.MaxValue;
        var qMax = float.MinValue;
        foreach (var source in _sources)
        {
            var q = Vector2.Dot(source, perp);
            qMin = MathF.Min(qMin, q);
            qMax = MathF.Max(qMax, q);
        }

        if (_sources.Count == 0)
        {
            shadow.BinStart = Array.Empty<int>();
            return;
        }

        qMin -= ShadowHalfWidth;
        shadow.QMin = qMin;

        // Per strip a sorted list of the positions (along the light) of the blockers that cover it.
        var bins = (int) MathF.Ceiling((qMax + ShadowHalfWidth - qMin) / ShadowBinWidth) + 2;
        if (shadow.BinStart.Length != bins + 1)
            shadow.BinStart = new int[bins + 1];
        else
            Array.Clear(shadow.BinStart);

        foreach (var source in _sources)
        {
            var q = Vector2.Dot(source, perp);
            var first = Math.Max(0, (int) MathF.Floor((q - ShadowHalfWidth - qMin) / ShadowBinWidth));
            var last = Math.Min(bins - 1, (int) MathF.Floor((q + ShadowHalfWidth - qMin) / ShadowBinWidth));

            for (var i = first; i <= last; i++)
            {
                shadow.BinStart[i + 1]++;
            }
        }

        for (var i = 0; i < bins; i++)
        {
            shadow.BinStart[i + 1] += shadow.BinStart[i];
        }

        var total = shadow.BinStart[bins];
        if (shadow.BinData.Length < total)
            shadow.BinData = new float[total + total / 4];

        if (_cursor.Length < bins)
            _cursor = new int[bins];

        Array.Copy(shadow.BinStart, _cursor, bins);

        foreach (var source in _sources)
        {
            var q = Vector2.Dot(source, perp);
            var p = Vector2.Dot(source, localDir);
            var first = Math.Max(0, (int) MathF.Floor((q - ShadowHalfWidth - qMin) / ShadowBinWidth));
            var last = Math.Min(bins - 1, (int) MathF.Floor((q + ShadowHalfWidth - qMin) / ShadowBinWidth));

            for (var i = first; i <= last; i++)
            {
                shadow.BinData[_cursor[i]++] = p;
            }
        }

        for (var i = 0; i < bins; i++)
        {
            var start = shadow.BinStart[i];
            var length = shadow.BinStart[i + 1] - start;
            if (length > 1)
                Array.Sort(shadow.BinData, start, length);
        }

        var exposed = ComputeLit(shadow, localDir);

        DebugInfo = $"Station sun: solid tiles={shadow.Solid.Count}, blockers={ownSources}+{_sources.Count - ownSources} (other grids), " +
                    $"shadow strips={bins}, windows={shadow.Windows.Count} (exposed {exposed}), lit tiles={shadow.Lit.Count}";
    }

    /// <summary>
    /// How strongly (0..1) the station's shadow covers a point: the distance behind the nearest blocker upstream of
    /// it in the same strip decides: full strength for <see cref="ShadowFullDistance"/> tiles, then a linear fade to
    /// nothing at <see cref="ShadowEndDistance"/>.
    /// </summary>
    private static float ShadowStrength(GridShadow shadow, Vector2i tile)
    {
        var center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);

        // Half a tile downstream of the blocker's centre at least.
        var distance = DistanceBehindBlocker(shadow, center, 0.5f);
        if (distance < 0f)
            return 0f;

        // Space enclosed by the station never gets light, however far it is from the blocker.
        if (shadow.Enclosed.Contains(tile))
            return 1f;

        return Fade(distance);
    }

    private static float Fade(float distance)
    {
        if (distance <= ShadowFullDistance)
            return 1f;

        if (distance >= ShadowEndDistance)
            return 0f;

        return 1f - (distance - ShadowFullDistance) / (ShadowEndDistance - ShadowFullDistance);
    }

    /// <summary>
    /// Distance along the light from the nearest blocker that is at least <paramref name="minGap"/> upstream of
    /// <paramref name="point"/> in the same strip, or -1 if there is none.
    /// </summary>
    private static float DistanceBehindBlocker(GridShadow shadow, Vector2 point, float minGap)
    {
        if (shadow.BinStart.Length == 0)
            return -1f;

        var bin = (int) MathF.Floor((Vector2.Dot(point, shadow.Perp) - shadow.QMin) / ShadowBinWidth);
        if (bin < 0 || bin + 1 >= shadow.BinStart.Length)
            return -1f;

        var start = shadow.BinStart[bin];
        var length = shadow.BinStart[bin + 1] - start;
        if (length == 0)
            return -1f;

        var p = Vector2.Dot(point, shadow.Dir);
        // Last blocker that is at least minGap upstream (the sandbox forbids Array.BinarySearch).
        var target = p - minGap;
        var lo = start;
        var hi = start + length - 1;
        var index = start - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (shadow.BinData[mid] <= target)
            {
                index = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (index < start)
            return -1f;

        return p - shadow.BinData[index];
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

        ComputeEnclosed(shadow);
    }

    /// <summary>
    /// Finds the empty space (and grilles, lattice) that the station's solid tiles enclose: it is not connected to the
    /// space outside the station, so no sunlight reaches it however large it is. Skipped while the tiles are unchanged.
    /// </summary>
    private void ComputeEnclosed(GridShadow shadow)
    {
        var hash = shadow.Solid.Count;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var tile in shadow.Solid)
        {
            unchecked
            {
                hash ^= tile.X * 73856093 ^ tile.Y * 19349663;
            }

            minX = Math.Min(minX, tile.X);
            minY = Math.Min(minY, tile.Y);
            maxX = Math.Max(maxX, tile.X);
            maxY = Math.Max(maxY, tile.Y);
        }

        if (shadow.EnclosedValid && hash == shadow.SolidHash)
            return;

        shadow.EnclosedValid = true;
        shadow.SolidHash = hash;
        shadow.Enclosed.Clear();

        if (shadow.Solid.Count == 0)
            return;

        // One tile of margin around the solid tiles, the margin ring is outside space and connects everything outside.
        minX--;
        minY--;
        maxX++;
        maxY++;

        var w = maxX - minX + 1;
        var h = maxY - minY + 1;
        if ((long) w * h > 6_000_000)
            return;

        var cells = w * h;
        if (_floodOutside.Length < cells)
        {
            _floodOutside = new bool[cells];
            _floodQueue = new int[cells];
        }
        else
        {
            Array.Clear(_floodOutside, 0, cells);
        }

        foreach (var tile in shadow.Solid)
        {
            _floodOutside[(tile.Y - minY) * w + (tile.X - minX)] = true;
        }

        // _floodOutside doubles as "visited or solid" while flooding.
        var head = 0;
        var tail = 0;
        _floodQueue[tail++] = 0;
        _floodOutside[0] = true;

        while (head < tail)
        {
            var cell = _floodQueue[head++];
            var x = cell % w;
            var y = cell / w;

            if (x > 0 && !_floodOutside[cell - 1]) { _floodOutside[cell - 1] = true; _floodQueue[tail++] = cell - 1; }
            if (x < w - 1 && !_floodOutside[cell + 1]) { _floodOutside[cell + 1] = true; _floodQueue[tail++] = cell + 1; }
            if (y > 0 && !_floodOutside[cell - w]) { _floodOutside[cell - w] = true; _floodQueue[tail++] = cell - w; }
            if (y < h - 1 && !_floodOutside[cell + w]) { _floodOutside[cell + w] = true; _floodQueue[tail++] = cell + w; }
        }

        // Everything the flood never reached and that is not solid is enclosed.
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (_floodOutside[y * w + x])
                    continue;

                shadow.Enclosed.Add(new Vector2i(x + minX, y + minY));
            }
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

                var exposure = Exposure(shadow, origin, start, -dir);
                if (exposure <= 0f)
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

                    shadow.Lit[tile] = shadow.Lit.GetValueOrDefault(tile) + weight * exposure;
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
    /// How exposed (0..1) a beam ray is: looking against the light we must reach open space soon and nothing solid may be
    /// in the way beyond it. Space tiles enclosed by the station (holes, cavities) fail the second check.
    /// </summary>
    private static float Exposure(GridShadow shadow, Vector2i origin, Vector2 start, Vector2 upstream)
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
                    return 0f;

                if (!shadow.Solid.Contains(tile))
                {
                    // Looking into a hole enclosed by the station: no light comes out of it.
                    if (shadow.Enclosed.Contains(tile))
                        return 0f;

                    reachedSpace = true;
                }
                else if (t > NearRange)
                    return 0f;

                continue;
            }

            if (shadow.Solid.Contains(tile) || shadow.Blocked.Contains(tile))
                return 0f;
        }

        if (!reachedSpace)
            return 0f;

        // Beyond the checked range the strips decide: a blocker far upstream shades the window like it shades
        // open space, with the same fade.
        var distance = DistanceBehindBlocker(shadow, start, NearRange + FarRange);
        return distance < 0f ? 1f : 1f - Fade(distance);
    }

    /// <summary>
    /// How much of the tile (0..1) is currently reached by sunlight through a window.
    /// </summary>
    public float GetLight(EntityUid grid, Vector2i tile)
    {
        return _cache.TryGetValue(grid, out var shadow) ? shadow.Lit.GetValueOrDefault(tile) : 0f;
    }

    /// <summary>
    /// Explains how the overlay treats a tile, for the debug overlay.
    /// </summary>
    public string Describe(EntityUid grid, Vector2i tile)
    {
        if (!_cache.TryGetValue(grid, out var shadow))
            return "no data";

        return $"solid={shadow.Solid.Contains(tile)} blocked={shadow.Blocked.Contains(tile)} " +
               $"window={shadow.WindowSet.Contains(tile)} shadow={GetShadow(shadow, tile):0.00} enclosed={shadow.Enclosed.Contains(tile)} " +
               $"sunlight={shadow.Lit.GetValueOrDefault(tile):0.00}";
    }

    /// <summary>
    /// Strength (0..1) of the station's cast shadow on a tile, before Alpha is applied.
    /// </summary>
    public float GetShadow(EntityUid grid, Vector2i tile)
    {
        return _cache.TryGetValue(grid, out var shadow) ? GetShadow(shadow, tile) : 0f;
    }

    /// <summary>
    /// Whether the tile is empty space (or grille, lattice) enclosed by the station's solid tiles.
    /// </summary>
    public bool IsEnclosed(EntityUid grid, Vector2i tile)
    {
        return _cache.TryGetValue(grid, out var shadow) && shadow.Enclosed.Contains(tile);
    }

    private static float GetShadow(GridShadow shadow, Vector2i tile)
    {
        if (shadow.Solid.Contains(tile))
            return 0f;

        return ShadowStrength(shadow, tile);
    }

    public IReadOnlyCollection<Vector2i>? GetWindows(EntityUid grid)
    {
        return _cache.TryGetValue(grid, out var shadow) ? shadow.Windows : null;
    }

    public IReadOnlyDictionary<Vector2i, float>? GetLitTiles(EntityUid grid)
    {
        return _cache.TryGetValue(grid, out var shadow) ? shadow.Lit : null;
    }

    private sealed class GridShadow
    {
        public readonly HashSet<Vector2i> Solid = new();
        public readonly HashSet<Vector2i> Blocked = new();
        public readonly HashSet<Vector2i> WindowSet = new();
        public readonly List<Vector2i> Windows = new();
        public readonly Dictionary<Vector2i, float> Lit = new();
        public readonly HashSet<Vector2i> Enclosed = new();
        public int SolidHash;
        public bool EnclosedValid;
        public int[] BinStart = Array.Empty<int>();
        public float[] BinData = Array.Empty<float>();
        public float QMin;
        public Vector2 Dir;
        public Vector2 Perp;
        public Vector2 Direction;
        public double Theta;
        public TimeSpan NextRescan;
        public TimeSpan NextDirectional;
    }
}
