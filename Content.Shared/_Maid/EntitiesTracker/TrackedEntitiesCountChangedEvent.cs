namespace Content.Shared._Maid.EntitiesTracker;

/// <summary>
/// Raised directed on the tracker entity when the count of tracked entities changes.
/// </summary>
[ByRefEvent]
public readonly record struct TrackedEntitiesCountChangedEvent(int OldCount, int NewCount);
