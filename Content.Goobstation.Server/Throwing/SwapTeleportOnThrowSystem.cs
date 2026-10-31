using System.Numerics;
using Content.Goobstation.Shared.Throwing;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Goobstation.Server.Throwing;

public sealed class SwapTeleportOnThrowSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Срабатывает при падении на пол
        SubscribeLocalEvent<SwapTeleportOnThrowComponent, LandEvent>(OnLand);
        // Срабатывает при ударе о препятствие/стену/существо
        SubscribeLocalEvent<SwapTeleportOnThrowComponent, ThrowDoHitEvent>(OnHit);
    }

    private void OnLand(Entity<SwapTeleportOnThrowComponent> ent, ref LandEvent args)
    {
        if (args.User != null)
            DoScram(ent, args.User.Value);
    }

    private void OnHit(Entity<SwapTeleportOnThrowComponent> ent, ref ThrowDoHitEvent args)
    {
        if (args.Component.Thrower != null)
            DoScram(ent, args.Component.Thrower.Value);
    }
    private void DoScram(Entity<SwapTeleportOnThrowComponent> ent, EntityUid user)
    {
        if (EntityManager.IsQueuedForDeletion(ent))
            return;

        var originPos = Transform(user).Coordinates;
        if (TryFindRandomTileInRange(originPos, 5, out var targetCoords))
        {
            _transform.SetCoordinates(user, targetCoords);
            _audio.PlayPvs(ent.Comp.OriginSound, originPos);
            _audio.PlayPvs(ent.Comp.TargetSound, targetCoords);
        }

        PredictedQueueDel(ent.Owner);
    }

    /// <summary>
    /// Ищет свободный проходимый тайл в радиусе maxDistance клеток (не ближе 3 клеток от игрока).
    /// </summary>
    private bool TryFindRandomTileInRange(EntityCoordinates centerCoords, int maxDistance, out EntityCoordinates result)
    {
        result = centerCoords;

        var tileRef = _turf.GetTileRef(centerCoords);
        if (tileRef == null)
            return false;

        var gridUid = tileRef.Value.GridUid;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var centerIndices = tileRef.Value.GridIndices;

        for (var i = 0; i < 25; i++)
        {
            var offsetX = _random.Next(-maxDistance, maxDistance + 1);
            var offsetY = _random.Next(-maxDistance, maxDistance + 1);

            // Не телепортируем прямо под себя
            if (Math.Abs(offsetX) < 3 && Math.Abs(offsetY) < 3)
                continue;

            var targetIndices = centerIndices + new Vector2i(offsetX, offsetY);

            // Проверяем проходимость тайла (нет стен, окон, шлюзов)
            if (!_turf.IsTileBlocked(gridUid, targetIndices, CollisionGroup.MobMask, grid))
            {
                // Правильный вызов через SharedMapSystem
                result = _map.GridTileToLocal(gridUid, grid, targetIndices);
                return true;
            }
        }
        return false;
    }
}