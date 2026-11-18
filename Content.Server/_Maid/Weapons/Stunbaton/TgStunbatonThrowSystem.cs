using Content.Goobstation.Common.Weapons.DelayedKnockdown;
using Content.Goobstation.Shared.Clothing;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Maid.Weapons.Stunbaton;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Jittering;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Random;

namespace Content.Server._Maid.Weapons.Stunbaton;

public sealed class TgStunbatonThrowSystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly SharedStutteringSystem _stutter = default!;
    [Dependency] private readonly TgStunbatonSystem _baton = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TgStunbatonComponent, TgStunbatonStunEvent>(OnStun);
        SubscribeLocalEvent<TgStunbatonComponent, ThrowDoHitEvent>(OnThrowHit);
    }

    private void OnStun(Entity<TgStunbatonComponent> ent, ref TgStunbatonStunEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        ApplyStunEffects(ent, args.Target);
    }

    private void OnThrowHit(Entity<TgStunbatonComponent> ent, ref ThrowDoHitEvent args)
    {
        if (!ent.Comp.Enabled || !_toggle.IsActivated(ent.Owner) || !_random.Prob(ent.Comp.ThrowStunChance))
            return;

        if (!TryComp<StaminaComponent>(args.Target, out var stamina))
            return;

        if (!TryComp<StunbatonComponent>(ent.Owner, out var stun) ||
            !TryComp<BatteryComponent>(ent.Owner, out var battery) ||
            !_battery.TryUseCharge(ent.Owner, stun.EnergyPerUse, battery))
            return;

        SoundSpecifier? sound = null;
        if (TryComp<StaminaDamageOnHitComponent>(ent.Owner, out var onHit))
            sound = onHit.Sound;

        _stamina.TakeStaminaDamage(args.Target, ent.Comp.ThrowStaminaDamage, stamina,
            source: args.Component.Thrower, with: ent.Owner, sound: sound);
        _baton.RegisterStun(ent, args.Target, args.Component.Thrower, predictedPopup: false);
    }

    private void ApplyStunEffects(Entity<TgStunbatonComponent> baton, EntityUid target)
    {
        var attempt = new DelayedKnockdownAttemptEvent();
        RaiseLocalEvent(target, attempt);
        if (!attempt.Cancelled)
        {
            var delayed = EnsureComp<DelayedKnockdownComponent>(target);
            delayed.Time = MathF.Min(baton.Comp.KnockdownDelay + attempt.DelayDelta, delayed.Time);
            delayed.KnockdownTime = MathF.Max(baton.Comp.KnockdownTime + attempt.KnockdownTimeDelta, delayed.KnockdownTime);
        }

        _jitter.DoJitter(target, TimeSpan.FromSeconds(baton.Comp.JitterTime), refresh: true);
        _stutter.DoStutter(target, TimeSpan.FromSeconds(baton.Comp.StutterTime), refresh: true);
    }
}
