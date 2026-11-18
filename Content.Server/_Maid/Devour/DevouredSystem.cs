using Content.Shared.Damage;
using Content.Shared.Devour.Components;
using Robust.Shared.Containers;

namespace Content.Server._Maid.Devour;

public sealed class DevouredSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DevourerComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<DevourerComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<DevouredComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    private void OnInserted(EntityUid uid, DevourerComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == DevourerComponent.StomachContainerId)
            EnsureComp<DevouredComponent>(args.Entity);
    }

    private void OnRemoved(EntityUid uid, DevourerComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == DevourerComponent.StomachContainerId)
            RemCompDeferred<DevouredComponent>(args.Entity);
    }

    private void OnBeforeDamageChanged(Entity<DevouredComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Damage.AnyPositive())
            args.Cancelled = true;
    }
}
