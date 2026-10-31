namespace Content.Shared._Maid.EntitiesTracker;

public sealed class EntitiesTrackerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TrackedEntityComponent, ComponentShutdown>(OnTrackedShutdown);
        SubscribeLocalEvent<EntitiesTrackerComponent, ComponentShutdown>(OnTrackerShutdown);
    }

    private void OnTrackedShutdown(Entity<TrackedEntityComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Tracker is { } tracker && TryComp<EntitiesTrackerComponent>(tracker, out var trackerComp))
            trackerComp.Entities.Remove(ent.Owner);
    }

    private void OnTrackerShutdown(Entity<EntitiesTrackerComponent> ent, ref ComponentShutdown args)
    {
        foreach (var tracked in ent.Comp.Entities)
        {
            if (TryComp<TrackedEntityComponent>(tracked, out var trackedComp) && trackedComp.Tracker == ent.Owner)
            {
                trackedComp.Tracker = null;
                RemComp<TrackedEntityComponent>(tracked);
            }
        }
    }

    /// <summary>
    /// Tracks an entity with the given tracker.
    /// </summary>
    public bool TrackEntity(Entity<EntitiesTrackerComponent?> tracker, Entity<TrackedEntityComponent?> entity)
    {
        var trackerUid = tracker.Owner;
        var entityUid = entity.Owner;

        if (TerminatingOrDeleted(trackerUid) || TerminatingOrDeleted(entityUid))
            return false;

        var trackerComp = tracker.Comp ?? EnsureComp<EntitiesTrackerComponent>(trackerUid);
        var trackedComp = entity.Comp ?? EnsureComp<TrackedEntityComponent>(entityUid);

        if (trackedComp.Tracker != null && trackedComp.Tracker != trackerUid)
        {
            if (TryComp<EntitiesTrackerComponent>(trackedComp.Tracker.Value, out var oldTrackerComp))
            {
                oldTrackerComp.Entities.Remove(entityUid);
            }
        }

        trackedComp.Tracker = trackerUid;
        return trackerComp.Entities.Add(entityUid);
    }

    /// <summary>
    /// Untracks an entity from whatever tracker it is currently attached to.
    /// </summary>
    public bool UntrackEntity(Entity<TrackedEntityComponent?> entity)
    {
        var entityUid = entity.Owner;
        if (!Resolve(entityUid, ref entity.Comp, false))
            return false;

        if (entity.Comp.Tracker is { } tracker && TryComp<EntitiesTrackerComponent>(tracker, out var trackerComp))
            trackerComp.Entities.Remove(entityUid);

        entity.Comp.Tracker = null;
        RemCompDeferred<TrackedEntityComponent>(entityUid);
        return true;
    }

    public int GetCount(Entity<EntitiesTrackerComponent?> tracker)
    {
        if (!Resolve(tracker.Owner, ref tracker.Comp, false))
            return 0;

        return tracker.Comp.Entities.Count;
    }

    public bool IsTracked(Entity<EntitiesTrackerComponent?> tracker, EntityUid entityUid)
    {
        if (!Resolve(tracker.Owner, ref tracker.Comp, false))
            return false;

        return tracker.Comp.Entities.Contains(entityUid);
    }
}
