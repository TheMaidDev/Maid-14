using Content.Server.Body.Systems;
using Content.Server.Popups;
using Content.Shared._Maid.Weapons.Melee.CritSystem;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Maid.Weapons.Melee.CritSystem;

public sealed class CritSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MaidCritComponent, MeleeHitEvent>(HandleHit);
    }

    private void HandleHit(EntityUid uid, MaidCritComponent component, MeleeHitEvent args)
    {
        if (args.HitEntities.Count is 0 or > 1)
            return;

        var target = args.HitEntities[0];

        if (!IsCriticalHit(uid, component))
            return;

        var total = args.BaseDamage.GetTotal();
        var damage = total * component.CritMultiplier;

        args.BonusDamage = new DamageSpecifier(
            _prototypeManager.Index(component.CritType),
            damage - total
        );

        _popup.PopupEntity($"Крит! +{damage - total} урона", args.User, args.User, PopupType.MediumCaution);
    }

    private bool IsCriticalHit(EntityUid uid, MaidCritComponent component)
    {
        var roll = _random.NextFloat();
        return roll < component.CritChance;
    }
}
