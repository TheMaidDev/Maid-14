using System.Collections.Generic;
using Content.Server._Maid.AdaptiveGameMode.Conditions;
using Content.Shared._Maid.Utils;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;
namespace Content.Server._Maid.AdaptiveGameMode;

/// <summary>
/// Gamerule component for the Adaptive game mode.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class AdaptiveRuleComponent : Component
{
    [DataField]
    public AdaptiveScore RoundstartTargetBudgetBase = new();

    [DataField]
    public List<AdaptiveBudgetSlopePoint> RoundstartTargetBudgetSlope = [];

    [DataField]
    public AdaptiveScore RoundstartScorePerPlayer = new();

    [DataField]
    public int RoundstartMaxRules = 5;

    [DataField]
    public bool RoundstartUsePotentialScore = true;

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
    [DataField, AutoPausedField]
    public TimeSpan MidroundSpawnAttemptAt = TimeSpan.Zero;

    /// <summary>
    /// Major midround rules can spawn only if `current score` is less than `target score * this`
    /// </summary>
    [DataField]
    public float MajorMidroundRulesMaxScorePercentage = 1f;

    /// <summary>
    /// Gamerules that can spawn midround.
    /// </summary>
    [DataField]
    public List<AdaptiveRuleParam> MidroundRules = new();

    /// <summary>
    /// See <see cref="MajorMidroundRulesMaxScorePercentage"/>
    /// </summary>
    [DataField]
    public List<AdaptiveRuleParam> MajorMidroundRules = new();

    [DataField]
    public List<AdaptiveSpawnedRule> SpawnedRules = new();

    /// <summary>
    /// Linear factor (k2) in rule weight multiplier formula: e^(-x*k2 - (x/s2)^p2).
    /// </summary>
    [DataField("k2")]
    public float K2 = 0f;

    /// <summary>
    /// Power exponent (p2) in rule weight multiplier formula: e^(-x*k2 - (x/s2)^p2).
    /// </summary>
    [DataField("p2")]
    public float P2 = 1f;

    /// <summary>
    /// Scale denominator (s2) in rule weight multiplier formula: e^(-x*k2 - (x/s2)^p2).
    /// </summary>
    [DataField("s2")]
    public float S2 = 17f;
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
