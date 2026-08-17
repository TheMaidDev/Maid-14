
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Static;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class AdaptiveScoreStaticComponent : Component
{
    [DataField]
    public ScoreSlope ChaosScore { get; set; } = new();

    [DataField]
    public ScoreSlope CombatScore { get; set; } = new();

    [DataField, AutoPausedField]
    public TimeSpan CreationTime { get; set; } = TimeSpan.Zero;

    [DataField]
    public List<IAdaptiveScoreCondition> Conditions { get; set; } = [];

    [DataField]
    public List<ProtoId<AdaptiveScoreConditionsTablePrototype>> ConditionTables { get; set; } = [];
}
