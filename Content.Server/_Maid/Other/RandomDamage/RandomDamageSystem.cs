using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Maid.Other.RandomDamage;

public sealed class RandomDamageSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomDamageComponent, MeleeHitEvent>(HandleHit);
    }

    private void HandleHit(Entity<RandomDamageComponent> ent, ref MeleeHitEvent args)
    {
        var damage = _random.NextFloat() * ent.Comp.Max;
        args.BonusDamage = new DamageSpecifier(_prototypeManager.Index<DamageTypePrototype>("Slash"), damage);
    }
}
