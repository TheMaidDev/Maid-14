namespace Content.Shared._Maid.EntitiesTracker;

[RegisterComponent]
public sealed partial class EntitiesTrackerComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public HashSet<EntityUid> Entities = new();
}
