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
        {
            var oldCount = trackerComp.Entities.Count;
            if (trackerComp.Entities.Remove(ent.Owner))
            {
                var ev = new TrackedEntitiesCountChangedEvent(oldCount, trackerComp.Entities.Count);
                RaiseLocalEvent(tracker, ref ev);
            }
        }
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
                var oldTracker = trackedComp.Tracker.Value;
                var oldTrackerCount = oldTrackerComp.Entities.Count;
                if (oldTrackerComp.Entities.Remove(entityUid))
                {
                    var oldEv = new TrackedEntitiesCountChangedEvent(oldTrackerCount, oldTrackerComp.Entities.Count);
                    RaiseLocalEvent(oldTracker, ref oldEv);
                }
            }
        }

        trackedComp.Tracker = trackerUid;
        var oldCount = trackerComp.Entities.Count;
        if (trackerComp.Entities.Add(entityUid))
        {
            var ev = new TrackedEntitiesCountChangedEvent(oldCount, trackerComp.Entities.Count);
            RaiseLocalEvent(trackerUid, ref ev);
            return true;
        }

        return false;
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
        {
            var oldCount = trackerComp.Entities.Count;
            if (trackerComp.Entities.Remove(entityUid))
            {
                var ev = new TrackedEntitiesCountChangedEvent(oldCount, trackerComp.Entities.Count);
                RaiseLocalEvent(tracker, ref ev);
            }
        }

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

    public bool TrackEntity(EntityUid tracker, EntityUid entity) =>
        TrackEntity((tracker, null), (entity, null));

    public bool UntrackEntity(EntityUid entity) =>
        UntrackEntity((entity, null));

    public bool UntrackEntity(Entity<EntitiesTrackerComponent?> tracker, EntityUid entityUid)
    {
        var trackerUid = tracker.Owner;
        if (!Resolve(trackerUid, ref tracker.Comp, false))
            return false;

        var oldCount = tracker.Comp.Entities.Count;
        var removed = tracker.Comp.Entities.Remove(entityUid);

        if (TryComp<TrackedEntityComponent>(entityUid, out var trackedComp) && trackedComp.Tracker == trackerUid)
        {
            trackedComp.Tracker = null;
            RemCompDeferred<TrackedEntityComponent>(entityUid);
        }

        if (removed)
        {
            var ev = new TrackedEntitiesCountChangedEvent(oldCount, tracker.Comp.Entities.Count);
            RaiseLocalEvent(trackerUid, ref ev);
        }

        return removed;
    }

    public bool UntrackEntity(EntityUid tracker, EntityUid entity) =>
        UntrackEntity((tracker, null), entity);

    public int GetCount(EntityUid tracker) =>
        GetCount((tracker, null));

    public bool IsTracked(EntityUid tracker, EntityUid entityUid) =>
        IsTracked((tracker, null), entityUid);
}
