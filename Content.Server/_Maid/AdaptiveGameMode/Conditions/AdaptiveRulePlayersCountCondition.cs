using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Maid.AdaptiveGameMode.Conditions;

/// <summary>
/// Condition that ensures the player count is within a specified min/max range before a game rule can be spawned.
/// </summary>
[DataDefinition]
public sealed partial class AdaptiveRulePlayersCountCondition : AdaptiveRuleCondition
{
    /// <summary>
    /// The minimum player count required for this rule to spawn.
    /// </summary>
    [DataField]
    public int Min = 0;

    /// <summary>
    /// The maximum player count allowed for this rule to spawn.
    /// </summary>
    [DataField]
    public int Max = int.MaxValue;

    public override bool Condition(AdaptiveRuleParam ruleParam, AdaptiveRuleComponent component, IEntityManager entityManager)
    {
        var playerManager = IoCManager.Resolve<ISharedPlayerManager>();
        var count = playerManager.PlayerCount;

        return Min <= count && Max >= count;
    }
}
