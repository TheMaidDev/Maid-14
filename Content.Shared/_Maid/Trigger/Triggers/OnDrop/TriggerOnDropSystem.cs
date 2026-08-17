using Content.Shared.Interaction.Events;
using Content.Shared.Trigger.Systems;

namespace Content.Shared._Maid.Trigger.Triggers.OnDrop;

public sealed class TriggerOnDropSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _triggerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnDropComponent, DroppedEvent>(OnDrop);
    }

    private void OnDrop(EntityUid uid, TriggerOnDropComponent component, DroppedEvent args)
    {
        _triggerSystem.Trigger(uid, args.User, component.KeyOut);
    }
}
