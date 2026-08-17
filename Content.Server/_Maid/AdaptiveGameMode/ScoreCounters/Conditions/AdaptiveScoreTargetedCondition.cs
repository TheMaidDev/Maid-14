using Content.Shared.Mind;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public abstract partial class AdaptiveScoreTargetedCondition : IAdaptiveScoreCondition
{
    [DataField]
    public AdaptiveScoreConditionTarget Target = AdaptiveScoreConditionTarget.Owner;

    public bool ConditionMet(EntityUid owner, EntityUid? controlledMob, Entity<MindComponent>? mind, IEntityManager entMan)
    {
        return ConditionMetOnTarget(IAdaptiveScoreCondition.ResolveTarget(Target, owner, controlledMob, mind), entMan);
    }

    protected abstract bool ConditionMetOnTarget(EntityUid? ent, IEntityManager entityManager);
}
