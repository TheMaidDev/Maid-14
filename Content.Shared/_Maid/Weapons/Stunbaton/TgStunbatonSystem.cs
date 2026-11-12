using Content.Goobstation.Common.Weapons;
using Content.Shared._Shitcode.Weapons.Misc;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Maid.Weapons.Stunbaton;

public sealed class TgStunbatonSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TgStunbatonComponent, LightAttackSpecialInteractionEvent>(OnLightAttack);
        SubscribeLocalEvent<TgStunbatonComponent, MeleeHitEvent>(OnMeleePrepare, before: [typeof(SharedStaminaSystem)]);
        SubscribeLocalEvent<TgStunbatonComponent, StaminaDamageMeleeHitEvent>(OnStaminaHit);
    }

    private void OnLightAttack(Entity<TgStunbatonComponent> ent, ref LightAttackSpecialInteractionEvent args)
    {
        if (!ent.Comp.Enabled || !_toggle.IsActivated(ent.Owner))
            return;

        if (args.Target is not { } target || !HasComp<DamageableComponent>(target))
        {
            args.Cancel = true;
            HoldSwing(ent.Owner);
            return;
        }

        if (!HasComp<StaminaComponent>(target))
            return;

        if (_timing.CurTime < ent.Comp.NextStun)
        {
            args.Cancel = true;
            HoldSwing(ent.Owner);
            _popup.PopupClient(Loc.GetString("tg-stunbaton-still-charging"), args.User, args.User);
            return;
        }

        if (IsMarked(target, args.User))
        {
            args.Cancel = true;
            HoldSwing(ent.Owner);
            _popup.PopupClient(Loc.GetString("tg-stunbaton-cant-stun"), args.User, args.User);
        }
    }

    private void OnMeleePrepare(Entity<TgStunbatonComponent> ent, ref MeleeHitEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        ent.Comp.SwingUser = args.IsHit && args.Direction == null ? args.User : null;
    }

    private void OnStaminaHit(Entity<TgStunbatonComponent> ent, ref StaminaDamageMeleeHitEvent args)
    {
        var user = ent.Comp.SwingUser;
        ent.Comp.SwingUser = null;

        if (!ent.Comp.Enabled || user == null || !_toggle.IsActivated(ent.Owner) || args.HitEntities.Count == 0 || args.Direction != null)
            return;

        foreach (var (target, _) in args.HitEntities)
            RegisterStun(ent, target, user);
    }

    public void RegisterStun(Entity<TgStunbatonComponent> baton, EntityUid target, EntityUid? user, bool predictedPopup = true)
    {
        if (!baton.Comp.Enabled)
            return;

        var until = _timing.CurTime + TimeSpan.FromSeconds(baton.Comp.Cooldown);
        baton.Comp.NextStun = until;
        Dirty(baton);

        if (user != null)
            Mark(target, user.Value, until);

        var selfMessage = Loc.GetString("tg-stunbaton-stun-self", ("user", user ?? baton.Owner), ("weapon", baton.Owner));
        var otherMessage = Loc.GetString("tg-stunbaton-stun-other", ("user", user ?? baton.Owner), ("target", target), ("weapon", baton.Owner));
        if (predictedPopup && user != null)
            _popup.PopupPredicted(selfMessage, otherMessage, target, user);
        else if (_net.IsServer)
            _popup.PopupEntity(otherMessage, target);

        var landed = new TgStunbatonStunEvent(target);
        RaiseLocalEvent(baton, ref landed);
    }

    private void HoldSwing(EntityUid weapon)
    {
        if (!TryComp<MeleeWeaponComponent>(weapon, out var melee))
            return;

        var retry = _timing.CurTime + TimeSpan.FromSeconds(0.4);
        if (melee.NextAttack >= retry)
            return;

        melee.NextAttack = retry;
        DirtyField(weapon, melee, nameof(MeleeWeaponComponent.NextAttack));
    }

    private void Mark(EntityUid target, EntityUid user, TimeSpan expires)
    {
        var mark = EnsureComp<TgStunbatonMarkComponent>(target);
        var netUser = GetNetEntity(user);
        var now = _timing.CurTime;

        mark.Marks.RemoveAll(entry => entry.User == netUser || entry.Expires <= now);
        mark.Marks.Add(new TgStunbatonMark { User = netUser, Expires = expires });
        Dirty(target, mark);
    }

    private bool IsMarked(EntityUid target, EntityUid user)
    {
        if (!TryComp<TgStunbatonMarkComponent>(target, out var mark))
            return false;

        var netUser = GetNetEntity(user);
        var now = _timing.CurTime;
        var changed = false;

        for (var i = mark.Marks.Count - 1; i >= 0; i--)
        {
            if (mark.Marks[i].Expires > now)
                continue;

            mark.Marks.RemoveAt(i);
            changed = true;
        }

        if (changed)
            Dirty(target, mark);

        foreach (var entry in mark.Marks)
        {
            if (entry.User == netUser)
                return true;
        }

        return false;
    }
}
