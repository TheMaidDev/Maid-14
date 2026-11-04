using System.Numerics;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Draws arrows showing the sunlight travel direction and highlights tiles that are open to the sun (not roofed).
/// Toggled with the <c>sunlightrays</c> command.
/// </summary>
public sealed class SunLightRaysOverlay : Overlay
{
    private const float ArrowSpacing = 4f;
    private const float ArrowLength = 2f;
    private const float HeadLength = 0.5f;

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;

    private List<Entity<MapGridComponent>> _grids = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public SunLightRaysOverlay()
    {
        IoCManager.InjectDependencies(this);
        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var bounds = args.WorldAABB;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(args.MapId, bounds, ref _grids);

        var lightDir = Vector2.Zero;

        foreach (var grid in _grids)
        {
            if (_entManager.TryGetComponent(grid.Owner, out SunShadowComponent? shadow) && shadow.Direction != Vector2.Zero)
                lightDir = Vector2.Normalize(shadow.Direction);

            // Tiles of station grids that sunlight reaches through windows.
            if (!_entManager.HasComponent<StationSunMarkerComponent>(grid.Owner) ||
                !_overlay.TryGetOverlay<StationSunShadowOverlay>(out var sunOverlay) ||
                sunOverlay.GetLitTiles(grid.Owner) is not { } lit)
            {
                continue;
            }

            handle.SetTransform(_xform.GetWorldMatrix(grid.Owner));
            var size = grid.Comp.TileSize;

            // Blue: tiles in the station's cast shadow. Green: windows. Yellow: sunlight through windows.
            var invGrid = _xform.GetInvWorldMatrix(grid.Owner);
            var c0 = Vector2.Transform(bounds.BottomLeft, invGrid);
            var c1 = Vector2.Transform(bounds.BottomRight, invGrid);
            var c2 = Vector2.Transform(bounds.TopRight, invGrid);
            var c3 = Vector2.Transform(bounds.TopLeft, invGrid);
            var minX = (int) MathF.Floor(MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X)) / size);
            var maxX = (int) MathF.Ceiling(MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X)) / size);
            var minY = (int) MathF.Floor(MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y)) / size);
            var maxY = (int) MathF.Ceiling(MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y)) / size);

            if ((long) (maxX - minX) * (maxY - minY) <= 400000)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        if (sunOverlay.IsShadowed(grid.Owner, new Vector2i(x, y)))
                            handle.DrawRect(new Box2(x * size, y * size, (x + 1) * size, (y + 1) * size), Color.Blue.WithAlpha(0.25f));
                    }
                }
            }

            if (sunOverlay.GetWindows(grid.Owner) is { } windows)
            {
                foreach (var tile in windows)
                {
                    handle.DrawRect(new Box2(tile.X * size, tile.Y * size, (tile.X + 1) * size, (tile.Y + 1) * size), Color.Lime.WithAlpha(0.35f));
                }
            }

            foreach (var (tile, light) in lit)
            {
                handle.DrawRect(new Box2(tile.X * size, tile.Y * size, (tile.X + 1) * size, (tile.Y + 1) * size), Color.Yellow.WithAlpha(0.4f * light));
            }
        }

        handle.SetTransform(Matrix3x2.Identity);

        if (lightDir == Vector2.Zero)
            return;

        // Arrows are in world space, shadow direction is a world-space vector.
        var perp = new Vector2(-lightDir.Y, lightDir.X);
        var color = Color.Orange;

        var startX = MathF.Floor(bounds.Left / ArrowSpacing) * ArrowSpacing;
        var startY = MathF.Floor(bounds.Bottom / ArrowSpacing) * ArrowSpacing;

        for (var x = startX; x <= bounds.Right; x += ArrowSpacing)
        {
            for (var y = startY; y <= bounds.Top; y += ArrowSpacing)
            {
                var from = new Vector2(x, y);
                var to = from + lightDir * ArrowLength;

                handle.DrawLine(from, to, color);
                handle.DrawLine(to, to - lightDir * HeadLength + perp * HeadLength * 0.5f, color);
                handle.DrawLine(to, to - lightDir * HeadLength - perp * HeadLength * 0.5f, color);
            }
        }
    }
}
