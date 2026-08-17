using Content.Shared.Hands.EntitySystems;
using Content.Shared.Trigger;
using Robust.Shared.Network;

namespace Content.Shared._Maid.Trigger.Effects.SpawnUniqueItem;

public sealed class SpawnUniqueItemOnTriggerSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpawnUniqueItemOnTriggerComponent, TriggerEvent>(Handle);
    }

    private void Handle(Entity<SpawnUniqueItemOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (!_net.IsServer)
            return;

        var target = ent.Comp.TargetUser ? args.User : ent.Owner;
        if (target is null)
            return;

        if (ent.Comp.Previous is not null)
            Del(ent.Comp.Previous);

        ent.Comp.Previous = SpawnAtPosition(ent.Comp.Proto, Transform(target.Value).Coordinates);

        if (ent.Comp is { TryPutInHands: true, Previous: not null })
        {
            _handsSystem.PickupOrDrop(target, ent.Comp.Previous.Value);
        }
    }
}
