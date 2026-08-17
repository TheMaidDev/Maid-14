using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Maid.AdaptiveGameMode.Conditions;

/// <summary>
/// Base class for conditions that determine if a midround rule can be spawned in the Adaptive game mode.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class AdaptiveRuleCondition
{
    /// <summary>
    /// Checks if this condition is met.
    /// </summary>
    /// <param name="ruleParam">The rule definition being evaluated.</param>
    /// <param name="component">The Adaptive rule component.</param>
    /// <param name="entityManager">The entity manager.</param>
    /// <returns>True if the condition is met and the rule can spawn, false otherwise.</returns>
    public abstract bool Condition(AdaptiveRuleParam ruleParam, AdaptiveRuleComponent component, IEntityManager entityManager);
}
