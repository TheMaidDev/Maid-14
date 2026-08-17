using System.Collections.Generic;
using Content.Server._Maid.AdaptiveGameMode.Conditions;
using Content.Shared._Maid.Utils;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;
namespace Content.Server._Maid.AdaptiveGameMode;

/// <summary>
/// Gamerule component for the Adaptive game mode.
/// </summary>
[RegisterComponent]
public sealed partial class AdaptiveRuleComponent : Component
{
    [DataField]
    public AdaptiveScore RoundstartTargetBudgetBase = new();

    [DataField]
    public List<AdaptiveBudgetSlopePoint> RoundstartTargetBudgetSlope = [];

    [DataField]
    public AdaptiveScore RoundstartScorePerPlayer = new();

    /// <summary>
    /// Gamerules that get added at round start.
    /// </summary>
    [DataField]
    public List<AdaptiveRuleParam> RoundstartRules = new();

    [DataField]
    public RangedNumber MidroundSpawnTimer = new();

    /// <summary>
    /// Time remaining until the next midround spawn attempt.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TimeUntilNextAttempt;
    /// <summary>
    /// The probability that we skip spawning a rule.
    /// </summary>
    [DataField]
    public float MidroundSpawnSkipProb = 0.6f;

    /// <summary>
    /// Gamerules that can spawn midround.
    /// </summary>
    [DataField]
    public List<AdaptiveRuleParam> MidroundRules = new();

    [DataField]
    public List<AdaptiveSpawnedRule> SpawnedRules = new();

    /// <summary>
    /// Decay factor for the score difference multiplier formula.
    /// </summary>
    [DataField]
    public float ScoreDifferenceMultiplierDecay = 1500f;

    /// <summary>
    /// Minimum weight multiplier when the score difference is large.
    /// </summary>
    [DataField]
    public float ScoreDifferenceMultiplierMin = 0.1f;
}

[DataDefinition]
public sealed partial class AdaptiveBudgetSlopePoint
{
    [DataField(required: true)]
    public float Time { get; set; }

    [DataField(required: true)]
    public AdaptiveScore Value { get; set; }
}

[DataDefinition]
public sealed partial class AdaptiveSpawnedRule
{
    [DataField(required: true)]
    public string RuleId = string.Empty;

    [DataField]
    public EntityUid Entity;

    [DataField]
    public TimeSpan SpawnTime;
}

[DataDefinition]
public sealed partial class AdaptiveRuleParam
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public float BaseWeight = 10f;

    [DataField]
    public List<AdaptiveRuleCondition> Conditions = new();
}
