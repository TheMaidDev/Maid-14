using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScoreAliveCondition : IAdaptiveScoreCondition
{
    [DataField]
    public bool AllowCritical = false;

    [DataField]
    public bool MustHaveState = false;

    public bool ConditionMet(EntityUid owner, EntityUid? mob, Entity<MindComponent>? mind, IEntityManager entMan)
    {
        if (mob is null)
            return false;

        if (!entMan.TryGetComponent(mob, out MobStateComponent? mobState))
            return !MustHaveState;

        var mobStateSystem = entMan.System<MobStateSystem>();

        if (AllowCritical && mobStateSystem.IsCritical(mob.Value, mobState))
            return true;

        return mobStateSystem.IsAlive(mob.Value, mobState);
    }
}
