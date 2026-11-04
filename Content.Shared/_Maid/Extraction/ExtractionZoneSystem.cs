using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Shared._Maid.Extraction;

public sealed class ExtractionZoneSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// Whether the user stands inside the square zone around the marker, on the same grid and not inside a container.
    /// </summary>
    public bool IsInZone(Entity<ExtractionZoneComponent> zone, EntityUid user)
    {
        if (_container.IsEntityOrParentInContainer(user))
            return false;

        var zoneXform = Transform(zone);
        if (zoneXform.GridUid is not { } grid
            || Transform(user).GridUid != grid
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var zoneTile = _map.TileIndicesFor(grid, gridComp, _transform.GetMapCoordinates(zone, zoneXform));
        var userTile = _map.TileIndicesFor(grid, gridComp, _transform.GetMapCoordinates(user));
        var delta = userTile - zoneTile;

        return Math.Abs(delta.X) <= zone.Comp.Range && Math.Abs(delta.Y) <= zone.Comp.Range;
    }
}
