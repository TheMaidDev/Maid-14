using System.Linq;
using Content.Server.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Maid.AdaptiveGameMode.Conditions;

/// <summary>
/// Condition that limits the number of active concurrent instances of a game rule.
/// </summary>
[DataDefinition]
public sealed partial class AdaptiveRuleMaxAtTheSameTimeCondition : AdaptiveRuleCondition
{
    [DataField]
    public int Max = 1;

    public override bool Condition(AdaptiveRuleParam ruleParam, AdaptiveRuleComponent component, IEntityManager entityManager)
    {
        var gameTicker = entityManager.System<GameTicker>();
        var count = gameTicker.GetActiveGameRules()
            .Count(ruleEntity =>
                (entityManager.GetComponentOrNull<MetaDataComponent>(ruleEntity)?.EntityPrototype?.ID ?? "") == ruleParam.Id
            );

        return count < Max;
    }
}
