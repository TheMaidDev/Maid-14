using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Spawners;

namespace Content.Shared._Maid.Weapons.HardlightSpear;

public sealed class PassThroughGlassSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PassThroughGlassComponent, PreventCollideEvent>(OnPreventCollision);
        SubscribeLocalEvent<MobStateComponent, PreventCollideEvent>(OnMobStatePreventCollision);
    }

    private void OnMobStatePreventCollision(Entity<MobStateComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.CurrentState == MobState.Dead && HasComp<EmbeddableProjectileComponent>(args.OtherEntity) &&
            HasComp<ThrownItemComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnPreventCollision(EntityUid uid, Weapons.HardlightSpear.PassThroughGlassComponent component, ref PreventCollideEvent args)
    {
        // Opaque collision mask doesn't work for EmbeddableProjectileComponent
        if (TryComp(args.OtherEntity, out FixturesComponent? fixtures) &&
            fixtures.Fixtures.All(fix => (fix.Value.CollisionLayer & (int) CollisionGroup.Opaque) == 0))
        {
            args.Cancelled = true;
        }
    }
}
