namespace Content.Shared._Maid.EntitiesTracker;

/// <summary>
/// Component placed on an entity that is tracked by an <see cref="EntitiesTrackerComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class TrackedEntityComponent : Component
{
    /// <summary>
    /// The entity tracker tracking this entity.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? Tracker;
}
