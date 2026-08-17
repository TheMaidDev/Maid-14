using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters;

[Prototype("adaptiveScoreConditionsTable")]
public sealed class AdaptiveScoreConditionsTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<IAdaptiveScoreCondition> Conditions { get; set; } = new();
}
