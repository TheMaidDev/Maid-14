using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Content.Server.Nuke;
using Content.Shared.Nuke;
using Content.Shared.Station;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using System.Collections.Generic;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Systems;

public sealed class AdaptiveScoreNukeSystem : EntitySystem
#if DEBUG
    , IAdaptiveBalanceInfoProvider
#endif
{
    [Dependency] private readonly SharedStationSystem _station = default!;

    private const float ChaosContribution = 50f;
    private const float CombatContribution = 0f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetAdaptiveScoreEvent>(OnGetAdaptiveScore);
    }

    private void OnGetAdaptiveScore(ref GetAdaptiveScoreEvent ev)
    {
        var query = EntityQueryEnumerator<NukeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var nuke, out var xform))
        {
            if (nuke.Status != NukeStatus.ARMED)
                continue;

            if (xform.GridUid == null || _station.GetOwningStation(uid) == null)
                continue;

            ev.Add(uid, ChaosContribution, CombatContribution);
        }
    }

#if DEBUG
    public IEnumerable<AdaptiveBalanceInfo> GetBalanceInfo()
    {
        yield return new()
        {
            Entity = "Nuke",
            Condition = "Armed",
            ChaosFrom = ChaosContribution,
            CombatFrom = CombatContribution,
        };
    }
#endif
}
