using Content.Shared.Trigger;

namespace Content.Shared._Maid.EntitiesTracker;

public sealed class UntrackEntityOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly EntitiesTrackerSystem _entitiesTracker = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UntrackEntityOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<UntrackEntityOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != null && !ent.Comp.KeysIn.Contains(args.Key))
            return;

        var target = ent.Comp.TargetUser ? args.User : ent.Owner;
        if (target == null)
            return;

        _entitiesTracker.UntrackEntity(target.Value);
        args.Handled = true;
    }
}
