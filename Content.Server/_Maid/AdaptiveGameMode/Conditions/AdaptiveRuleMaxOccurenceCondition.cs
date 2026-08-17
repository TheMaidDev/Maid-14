using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Maid.AdaptiveGameMode.Conditions;

/// <summary>
/// Condition that limits the total number of times a game rule can be spawned during a round.
/// </summary>
[DataDefinition]
public sealed partial class AdaptiveRuleMaxOccurenceCondition : AdaptiveRuleCondition
{
    /// <summary>
    /// The maximum number of times this rule can be spawned.
    /// </summary>
    [DataField]
    public int Amount;

    public override bool Condition(AdaptiveRuleParam ruleParam, AdaptiveRuleComponent component, IEntityManager entityManager)
    {
        var count = component.SpawnedRules.Count(spawned => spawned.RuleId == ruleParam.Id);

        return count < Amount;
    }
}
