using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.AdaptiveGameMode.Conditions;

/// <summary>
/// Condition that ensures a game rule is not spawned again within a certain cooldown period.
/// </summary>
[DataDefinition]
public sealed partial class AdaptiveRuleCooldownCondition : AdaptiveRuleCondition
{
    /// <summary>
    /// Cooldown period in seconds.
    /// </summary>
    [DataField]
    public float Cooldown;

    public override bool Condition(AdaptiveRuleParam ruleParam, AdaptiveRuleComponent component, IEntityManager entityManager)
    {
        var timing = IoCManager.Resolve<IGameTiming>();
        var lastSpawn = TimeSpan.Zero;

        foreach (var spawned in component.SpawnedRules)
        {
            if (spawned.RuleId == ruleParam.Id)
            {
                if (spawned.SpawnTime > lastSpawn)
                    lastSpawn = spawned.SpawnTime;
            }
        }

        if (lastSpawn == TimeSpan.Zero)
            return true;

        return (timing.CurTime - lastSpawn).TotalSeconds >= Cooldown;
    }
}
