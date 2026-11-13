using Robust.Shared.GameStates;
using Content.Shared.Trigger.Components.Effects;

namespace Content.Shared._Maid.EntitiesTracker;

/// <summary>
/// Untracks this entity (or the trigger user) from its tracker when triggered.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UntrackEntityOnTriggerComponent : BaseXOnTriggerComponent
{
}
