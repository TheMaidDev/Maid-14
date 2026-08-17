using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Collector;

[RegisterComponent]
public sealed partial class AdaptiveScoreCollectorComponent : Component
{
    [DataField]
    public float ChaosScore { get; set; } = 0;

    [DataField]
    public float CombatScore { get; set; } = 0;

    [DataField]
    public string? EnumerateComponent { get; set; }

    [DataField]
    public List<IAdaptiveScoreCondition> Conditions { get; set; } = [];

    [DataField]
    public List<ProtoId<AdaptiveScoreConditionsTablePrototype>> ConditionTables { get; set; } = [];
}
