using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using System.Collections.Generic;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Systems;

public sealed class AdaptiveScorePlayersAmountCounter : EntitySystem, IAdaptiveBalanceInfoProvider
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;

    private const float ChaosContribution = -2f;
    private const float CombatContribution = -2f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetAdaptiveScoreEvent>(OnGetAdaptiveScore);
    }

    private void OnGetAdaptiveScore(ref GetAdaptiveScoreEvent ev)
    {
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var actor, out var mobState, out var xform))
        {
            if (actor.PlayerSession.Status != Robust.Shared.Enums.SessionStatus.InGame)
                continue;

            if (!_mobState.IsAlive(uid, mobState))
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
            Entity = "Crew Member",
            Condition = "Alive + Controlled",
            ChaosFrom = ChaosContribution,
            CombatFrom = CombatContribution,
        };
    }
#endif
}
