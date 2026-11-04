using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Rotates grids with <see cref="GridSpinComponent"/> around the centre of their tiles.
/// </summary>
public sealed class GridSpinSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GridSpinComponent, TransformComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var spin, out var xform, out var grid))
        {
            if (spin.DegreesPerSecond == 0f)
                continue;

            var delta = Angle.FromDegrees(spin.DegreesPerSecond * frameTime);
            var center = grid.LocalAABB.Center;

            // Keep the visual centre of the grid in place, rotation alone would swing it around the grid origin.
            var before = Vector2.Transform(center, _xform.GetWorldMatrix(uid));
            _xform.SetLocalRotation(uid, xform.LocalRotation + delta, xform);
            var after = Vector2.Transform(center, _xform.GetWorldMatrix(uid));
            _xform.SetWorldPosition(uid, _xform.GetWorldPosition(xform) + (before - after));
        }
    }
}
