using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Collections.Generic;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScoreHasComponentCondition : AdaptiveScoreTargetedCondition
{
    [DataField(required: true)]
    public List<string> Components = [];

    protected override bool ConditionMetOnTarget(EntityUid? ent, IEntityManager entMan)
    {
        if (ent is null)
            return false;

        var compFactory = entMan.ComponentFactory;
        foreach (var compName in Components)
        {
            if (!compFactory.TryGetRegistration(compName, out var registration))
                return false;

            if (!entMan.HasComponent(ent, registration.Type))
                return false;
        }

        return true;
    }
}
