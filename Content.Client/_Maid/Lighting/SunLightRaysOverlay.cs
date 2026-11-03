using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
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

    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedRoofSystem _roof;
    private readonly SharedTransformSystem _xform;

    private readonly List<Entity<MapGridComponent>> _grids = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public SunLightRaysOverlay()
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entManager.System<EntityLookupSystem>();
        _mapSystem = _entManager.System<SharedMapSystem>();
        _roof = _entManager.System<SharedRoofSystem>();
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

            // Tiles open to the sun on grids with per-tile roofs.
            if (!_entManager.TryGetComponent(grid.Owner, out RoofComponent? roof) ||
                _entManager.HasComponent<ImplicitRoofComponent>(grid.Owner))
            {
                continue;
            }

            handle.SetTransform(_xform.GetWorldMatrix(grid.Owner));

            var roofEnt = (grid.Owner, grid.Comp, roof);
            var tiles = _mapSystem.GetTilesEnumerator(grid.Owner, grid.Comp, bounds);

            while (tiles.MoveNext(out var tileRef))
            {
                if (_roof.IsRooved(roofEnt, tileRef.GridIndices))
                    continue;

                handle.DrawRect(_lookup.GetLocalBounds(tileRef, grid.Comp.TileSize), Color.Yellow.WithAlpha(0.25f));
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
