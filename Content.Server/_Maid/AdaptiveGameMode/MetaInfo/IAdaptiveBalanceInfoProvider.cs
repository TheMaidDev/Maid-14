using System.Collections.Generic;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;

namespace Content.Server._Maid.AdaptiveGameMode.MetaInfo;


// THIS IS ONLY FOR BALANCING PURPOSES IN DEV ENV!
public interface IAdaptiveBalanceInfoProvider
{
    #if DEBUG
    IEnumerable<AdaptiveBalanceInfo> GetBalanceInfo();
    #endif
}
#if DEBUG
public struct AdaptiveBalanceInfo
{
    public string Entity;
    public string Condition;
    public float? ChaosFrom;
    public float? ChaosTo;
    public float? ChaosDuration;
    public float? CombatFrom;
    public float? CombatTo;
    public float? CombatDuration;

    public override string ToString() =>
        $"{Entity},{Condition},{ChaosFrom},{ChaosTo},{ChaosDuration},{CombatFrom},{CombatTo},{CombatDuration}";

    public static AdaptiveBalanceInfo FromSlope(string entity, string condition, ScoreSlope chaos, ScoreSlope combat)
    {
        return new()
        {
            Entity = entity,
            Condition = condition,
            ChaosFrom = chaos.Base,
            ChaosTo = chaos.Target,
            ChaosDuration = chaos.Target.HasValue ? (float)chaos.In.TotalSeconds : null,
            CombatFrom = combat.Base,
            CombatTo = combat.Target,
            CombatDuration = combat.Target.HasValue ? (float)combat.In.TotalSeconds : null,
        };
    }
}
#endif
