using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Maid.Weapons.EnergyGun;

public abstract class SharedTwoModeEnergyGunSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TwoModeEnergyGunComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, GunRefreshModifiersEvent>(OnRefreshModifiers);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<TwoModeEnergyGunComponent, GunShotEvent>(OnGunShot);
    }

    private void OnMapInit(Entity<TwoModeEnergyGunComponent> ent, ref MapInitEvent args)
    {
        ApplyMode(ent);
    }

    private void OnAfterState(Entity<TwoModeEnergyGunComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // The projectile prototype isn't networked, keep the predicted shots right.
        if (GetMode(ent) is { } mode && TryComp<ProjectileBatteryAmmoProviderComponent>(ent, out var battery))
            battery.Prototype = mode.Proto;
    }

    private void OnUseInHand(Entity<TwoModeEnergyGunComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Modes.Count < 2)
            return;

        args.Handled = true;

        ent.Comp.CurrentMode = (ent.Comp.CurrentMode + 1) % ent.Comp.Modes.Count;
        Dirty(ent);

        ApplyMode(ent);
        _audio.PlayPredicted(ent.Comp.ToggleSound, ent, args.User);
    }

    private void ApplyMode(Entity<TwoModeEnergyGunComponent> ent)
    {
        if (GetMode(ent) is not { } mode)
            return;

        if (TryComp<ProjectileBatteryAmmoProviderComponent>(ent, out var battery))
            battery.Prototype = mode.Proto;

        _item.SetHeldPrefix(ent, mode.HeldPrefix);
        _appearance.SetData(ent, TwoModeEnergyGunVisuals.Mode, ent.Comp.CurrentMode);
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnExamined(Entity<TwoModeEnergyGunComponent> ent, ref ExaminedEvent args)
    {
        if (GetMode(ent) is not { } mode)
            return;

        args.PushMarkup(Loc.GetString("gun-twomode-mode-examine", ("color", "red"), ("mode", Loc.GetString(mode.Name))));
    }

    private void OnRefreshModifiers(Entity<TwoModeEnergyGunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (GetMode(ent)?.SoundGunshot is { } sound)
            args.SoundGunshot = sound;
    }

    private void OnAttemptShoot(Entity<TwoModeEnergyGunComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled ||
            GetMode(ent) is not { } mode ||
            !TryComp<ProjectileBatteryAmmoProviderComponent>(ent, out var battery))
        {
            return;
        }

        // Empty guns click as usual, a gun with some charge left just can't afford this mode.
        if (battery.Shots == 0 || battery.Shots >= mode.Charges)
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("egun-not-enough-charges");
    }

    private void OnGunShot(Entity<TwoModeEnergyGunComponent> ent, ref GunShotEvent args)
    {
        if (GetMode(ent) is not { } mode ||
            mode.Charges <= 1 ||
            !TryComp<ProjectileBatteryAmmoProviderComponent>(ent, out var battery))
        {
            return;
        }

        // The battery already took one charge per shot.
        var extra = (mode.Charges - 1) * args.Ammo.Count;
        if (extra <= 0)
            return;

        battery.Shots = Math.Max(0, battery.Shots - extra);
        Dirty(ent, battery);

        _appearance.SetData(ent, AmmoVisuals.HasAmmo, battery.Shots != 0);
        _appearance.SetData(ent, AmmoVisuals.AmmoCount, battery.Shots);

        var updateAmmoEv = new UpdateClientAmmoEvent();
        RaiseLocalEvent(ent, ref updateAmmoEv);

        TakeExtraCharge(ent, extra * battery.FireCost);
    }

    /// <summary>
    /// Takes the charge of the extra shots out of the battery. Batteries aren't predicted, so this is server only.
    /// </summary>
    protected virtual void TakeExtraCharge(EntityUid uid, float charge)
    {
    }

    public TwoModeEnergyGunMode? GetMode(TwoModeEnergyGunComponent comp)
    {
        if (comp.CurrentMode < 0 || comp.CurrentMode >= comp.Modes.Count)
            return null;

        return comp.Modes[comp.CurrentMode];
    }

    private TwoModeEnergyGunMode? GetMode(Entity<TwoModeEnergyGunComponent> ent)
    {
        return GetMode(ent.Comp);
    }
}
