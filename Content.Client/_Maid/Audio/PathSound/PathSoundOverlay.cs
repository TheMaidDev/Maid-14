using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Debug overlay drawing the tile sound propagation field as per-tile rects, hue by path length, opacity by weight.
/// </summary>
public sealed class PathSoundOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    private readonly PathSoundSystem _system;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public PathSoundOverlay(PathSoundSystem system)
    {
        IoCManager.InjectDependencies(this);

        _system = system;
        _transform = _entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.FieldReady)
            return;

        if (_player.LocalEntity is not { } player ||
            !_entManager.TryGetComponent(player, out TransformComponent? playerXform))
            return;

        var gridUid = _system.FieldGrid;
        if (gridUid is not { } uid ||
            playerXform.GridUid != uid ||
            playerXform.MapID != args.MapId)
            return;

        if (!_entManager.TryGetComponent(uid, out MapGridComponent? gridComp))
            return;

        var worldHandle = args.WorldHandle;
        var seed = _system.FieldSeed;
        var radius = Math.Max(_system.Radius, 1);
        var tileSize = gridComp.TileSize;

        worldHandle.SetTransform(_transform.GetWorldMatrix(uid));

        for (var x = seed.X - radius; x <= seed.X + radius; x++)
        {
            for (var y = seed.Y - radius; y <= seed.Y + radius; y++)
            {
                var indices = new Vector2i(x, y);
                if (!_system.TryGetTile(indices, out var sumWeight, out var pathLen))
                    continue;

                var tile = new Box2(indices * tileSize, (indices + Vector2i.One) * tileSize);

                if (indices == seed)
                {
                    worldHandle.DrawRect(tile, Color.White.WithAlpha(0.6f));
                    worldHandle.DrawRect(tile, Color.White, filled: false);
                }
                else
                {
                    var hue = pathLen / (float) radius * 0.33f;
                    var color = Color.FromHsv(new Vector4(hue, 0.9f, 1f, 0.35f + MathF.Min(sumWeight, 1f) * 0.45f));
                    worldHandle.DrawRect(tile, color);
                }
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }
}
