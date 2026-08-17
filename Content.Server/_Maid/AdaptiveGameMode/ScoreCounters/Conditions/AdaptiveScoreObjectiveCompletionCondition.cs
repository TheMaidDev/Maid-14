using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Objectives.Systems;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

[DataDefinition]
public sealed partial class AdaptiveScoreObjectiveCompletionCondition : IAdaptiveScoreCondition
{
    [DataField]
    public bool Completed = false;

    public bool ConditionMet(EntityUid owner, EntityUid? mob, Entity<MindComponent>? mind, IEntityManager entMan)
    {
        var objectivesSystem = entMan.System<SharedObjectivesSystem>();

        // Query all minds to locate the one that owns this objective
        var query = entMan.EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var mindId, out var mindComp))
        {
            if (mindComp.Objectives.Contains(owner))
            {
                return objectivesSystem.IsCompleted(owner, (mindId, mindComp)) == Completed;
            }
        }

        return false;
    }
}
