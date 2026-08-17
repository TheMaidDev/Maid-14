using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScorePlayerUncuffedCondition : IAdaptiveScoreCondition
{
    public bool ConditionMet(EntityUid owner, EntityUid? mob, Entity<MindComponent>? mind, IEntityManager entMan)
    {
        if (mob is null)
            return true;

        if (entMan.TryGetComponent<CuffableComponent>(mob.Value, out var cuffable))
        {
            var cuffs = entMan.System<SharedCuffableSystem>();
            if (cuffs.IsCuffed((mob.Value, cuffable)))
                return false;
        }

        return true;
    }
}
