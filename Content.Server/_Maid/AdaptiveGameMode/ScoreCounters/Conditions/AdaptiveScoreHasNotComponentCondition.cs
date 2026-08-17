using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Collections.Generic;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScoreHasNotComponentCondition : AdaptiveScoreTargetedCondition
{
    [DataField(required: true)]
    public List<string> Components { get; set; } = [];

    protected override bool ConditionMetOnTarget(EntityUid? mob, IEntityManager entMan)
    {
        if (mob == null)
            return true;

        var compFactory = IoCManager.Resolve<IComponentFactory>();
        foreach (var compName in Components)
        {
            if (!compFactory.TryGetRegistration(compName, out var registration))
                continue;

            if (entMan.HasComponent(mob.Value, registration.Type))
                return false;
        }
        return true;
    }
}
