using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScoreOnStationCondition : IAdaptiveScoreCondition
{
    [DataField]
    public bool OnGrid = false;
    public bool ConditionMet(EntityUid owner, EntityUid? mob, Entity<MindComponent>? mind, IEntityManager entMan)
    {
        if (mob is null)
            return false;

        var station = entMan.System<SharedStationSystem>();
        var transform = entMan.System<SharedTransformSystem>();

        if (OnGrid)
            return station.GetOwningStation(mob) is not null;

        if (!entMan.TryGetComponent(mob, out TransformComponent? transformComp))
            return false;

        return station.GetStationInMap(transform.GetMapId((mob.Value, transformComp))) is not null;
    }
}
