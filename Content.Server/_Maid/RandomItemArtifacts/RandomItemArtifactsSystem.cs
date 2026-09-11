using System.Linq;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Item;
using Content.Shared.Station.Components;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Random;

namespace Content.Server._Maid.RandomItemArtifacts;

public sealed class RandomItemArtifactsSystem : StationEventSystem<RandomItemArtifactsRuleComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;

    protected override void Started(EntityUid uid, RandomItemArtifactsRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var entities = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (entities.MoveNext(out var ent, out var item, out var xform))
        {
            if (xform.Anchored)
                continue;

            if (_station.GetOwningStation(ent, xform) is null)
                continue;

            if (_random.Prob(component.ConversionChance))
                EnsureComp<XenoArtifactComponent>(ent);
        }
    }
}
