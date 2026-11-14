using System.Numerics;
using Content.Shared._Maid.Mood;
using Content.Shared._Maid.RakeTrap;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Camera;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Physics;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Content.Server.Chat.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Maid.RakeTrap;

/// <summary>
///     Server logic for the rake trap: deploying in front of the user, the cock-then-whack
///     sequence when someone steps on it, and disarming via a doafter.
/// </summary>
public sealed class RakeTrapSystem : SharedRakeTrapSystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, StepTriggeredOnEvent>(OnStepTriggered);
        SubscribeLocalEvent<RakeTrapComponent, RakeDisarmDoAfterEvent>(OnDisarmDoAfter);
    }

    protected override void OnDeployTry(Entity<RakeTrapComponent> ent, EntityUid user)
    {
        var xform = Transform(user);

        // The tile in front of the user (mime invisible-wall pattern).
        var offset = xform.LocalRotation.ToWorldVec();
        var coords = xform.Coordinates.Offset(offset).SnapToGrid(EntityManager);
        var tile = _turf.GetTileRef(coords);

        if (tile == null || _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable | CollisionGroup.Opaque))
        {
            _popup.PopupEntity(Loc.GetString("rake-deploy-blocked"), ent, user);
            return;
        }

        if (!_hands.TryDrop(user, ent, coords))
            return;

        _transform.SetCoordinates(ent, coords);

        ent.Comp.Armed = true;
        _audio.PlayPvs(ent.Comp.DeploySound, ent);
        _popup.PopupEntity(Loc.GetString("rake-deployed"), ent, user);
    }

    protected override void OnArmedClicked(Entity<RakeTrapComponent> ent, EntityUid user)
    {
        var args = new DoAfterArgs(EntityManager, user, ent.Comp.DisarmTime, new RakeDisarmDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("rake-disarm-start"), ent, user);
    }

    private void OnDisarmDoAfter(Entity<RakeTrapComponent> ent, ref RakeDisarmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!ent.Comp.Armed)
            return;

        ent.Comp.Armed = false;
        ent.Comp.HitAt = null;
        _audio.PlayPvs(ent.Comp.DisarmSound, ent);
        _popup.PopupEntity(Loc.GetString("rake-disarmed"), ent, args.User);
    }

    private void OnStepTriggered(Entity<RakeTrapComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!ent.Comp.Armed)
            return;

        // Disarm immediately so nothing else can set it off while the rake is cocking.
        ent.Comp.Armed = false;
        ent.Comp.HitAt = _timing.CurTime;
        _audio.PlayPvs(ent.Comp.CockSound, ent);

        var rake = ent.Owner;
        var victim = args.Tripper;
        Timer.Spawn(ent.Comp.CockTime, () => Whack(rake, victim));
    }

    private void Whack(EntityUid rake, EntityUid victim)
    {
        if (TerminatingOrDeleted(rake) || !TryComp<RakeTrapComponent>(rake, out var comp))
            return;

        if (TerminatingOrDeleted(victim))
            return;

        // Head hit.
        _damage.TryChangeDamage(victim, comp.Damage, origin: rake, targetPart: TargetBodyPart.Head);

        // Stunned (figuratively): down for a while, crawling, blurred vision, muffled audio.
        _stun.TryKnockdown(victim, comp.KnockdownTime, refresh: true, drop: true);
        _status.TryAddStatusEffect<BlurryVisionComponent>(victim, "BlurryVision", comp.BlurTime, true);

        var muffled = EnsureComp<RakeMuffledComponent>(victim);
        muffled.Gain = comp.MuffleGain;
        Timer.Spawn(comp.MuffleTime, () =>
        {
            if (!TerminatingOrDeleted(victim))
                RemComp<RakeMuffledComponent>(victim);
        });

        RaiseLocalEvent(victim, new MoodEffectEvent(comp.Mood));
        _chat.TryEmoteWithChat(victim, "Scream", forceEmote: true);

        // Camera kick away from the rake — up in the face.
        var rakePos = _transform.GetWorldPosition(rake);
        var victimPos = _transform.GetWorldPosition(victim);
        var delta = victimPos - rakePos;
        var dir = delta.LengthSquared() > 0.01f ? delta.Normalized() : Vector2.UnitY;
        _recoil.KickCamera(victim, dir * 1.2f);

        _audio.PlayPvs(comp.HitSound, rake);
        _popup.PopupEntity(Loc.GetString("rake-hit"), victim, victim);

        // Let the whack animation finish before the rake is shown lying down again.
        Timer.Spawn(comp.ResetVisualTime, () =>
        {
            if (TryComp<RakeTrapComponent>(rake, out var c))
                c.HitAt = null;
        });
    }
}
