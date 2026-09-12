using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._Maid.Chaplain;
using Content.Shared.Ghost;

namespace Content.Server._Maid.Chaplain;

public sealed class NullRodSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HolyNullRodComponent, WeaponSelectedEvent>(OnWeaponSelected);
    }

    private void OnWeaponSelected(Entity<HolyNullRodComponent> ent, ref WeaponSelectedEvent args)
    {
        var entity = args.Actor;
        if (args.SelectedWeapon == string.Empty)
            return;

        var hasHoly = HasComp<HolyComponent>(entity);
        var hasGhost = HasComp<GhostComponent>(entity);
        if (!hasHoly && !hasGhost)
        {
            _popup.PopupEntity($"Вам не хватает веры, чтобы использовать {Name(ent)}", entity, entity);
            return;
        }

        var weapon = Spawn(args.SelectedWeapon, Transform(entity).Coordinates);
        if (weapon == EntityUid.Invalid)
            return;

        EnsureComp<HolyWeaponComponent>(weapon);
        Del(ent);
        _hands.PickupOrDrop(entity, weapon, true, false, false);
    }
}
