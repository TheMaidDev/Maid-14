using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Events;
using Content.Server.Storage.EntitySystems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Server.Containers;
using Robust.Server.Player;
using Robust.Shared.Random;

namespace Content.Server._Maid.RandomItemArtifacts;

public sealed class RandomItemArtifactsSystem : StationEventSystem<RandomItemArtifactsRuleComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    protected override void Started(EntityUid uid, RandomItemArtifactsRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Pick random player
        var players = _playerManager.Sessions
            .Where(session => _gameTicker.PlayerGameStatuses.TryGetValue(session.UserId, out var status)
                              && status == PlayerGameStatus.JoinedGame)
            .Where(session => TryComp(session.AttachedEntity, out MobStateComponent? state) && state.CurrentState == MobState.Alive)
            .ToList();

        _random.Shuffle(players);

        void IterateOverContainer(EntityUid ent, List<EntityUid> items, int iter = 0)
        {
            if (iter > 5)
                return;

            if (HasComp<ItemComponent>(ent) && !HasComp<MindContainerComponent>(ent))
                items.Add(ent);

            // Inventory
            {
                var enumerator = _inventory.GetSlotEnumerator(ent);
                while (enumerator.MoveNext(out var slot))
                {
                    foreach (var containedEntity in slot.ContainedEntities)
                    {
                        IterateOverContainer(containedEntity, items, iter + 1);
                    }
                }
            }

            // Hands
            {
                foreach (var item in _hands.EnumerateHeld(ent))
                {
                    IterateOverContainer(item, items, iter + 1);
                }
            }

            // Storage
            {
                if (TryComp(ent, out StorageComponent? storage))
                {
                    foreach (var item in storage.Container.ContainedEntities)
                    {
                        IterateOverContainer(item, items, iter + 1);
                    }
                }
            }
        }

        foreach (var player in players)
        {
            if (player.AttachedEntity is not {} ent)
                continue;

            if (!_mobStateSystem.IsAlive(ent))
                continue;

            var containedEntities = new List<EntityUid>();

            IterateOverContainer(ent, containedEntities);

            if (containedEntities.Count <= 0)
                continue; // No items on that player, move to next one

            var item = _random.Pick(containedEntities);

            EnsureComp<XenoArtifactComponent>(item);
            _popupSystem.PopupEntity(
                Loc.GetString(
                    "random-artifacts-spawn-popup",
                    ("item", Name(item))
                ),
                ent,
                player,
                PopupType.MediumCaution
            );
            return;
        }
    }
}
