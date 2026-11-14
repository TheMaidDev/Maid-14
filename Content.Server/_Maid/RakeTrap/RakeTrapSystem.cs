using System.Numerics;
using Content.Shared._Maid.Mood;
using Content.Shared._Maid.RakeTrap;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Camera;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Content.Server.Chat.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
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
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, StepTriggeredOnEvent>(OnStepTriggered);
        SubscribeLocalEvent<RakeTrapComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<RakeTrapComponent, RakeDisarmDoAfterEvent>(OnDisarmDoAfter);
    }

    /// <summary>
    ///     Trigger the moment the victim's fixture touches the rake instead of waiting for
    ///     StepTriggerSystem's per-tick intersection-ratio check — makes the whack feel instant.
    ///     StepTriggerSystem's own trigger is the fallback; it no-ops because we disarm first.
    /// </summary>
    private void OnStartCollide(Entity<RakeTrapComponent> ent, ref StartCollideEvent args)
    {
        if (!ent.Comp.Armed || !args.OtherFixture.Hard)
            return;

        // Only mobs step on rakes (thrown items and other clutter don't set them off).
        if (!HasComp<MobStateComponent>(args.OtherEntity))
            return;

        // The installer walks over their own trap freely.
        if (args.OtherEntity == ent.Comp.Installer)
            return;

        ent.Comp.Armed = false;
        ent.Comp.HitAt = _timing.CurTime;
        Dirty(ent, ent.Comp);

        Whack(ent.Owner, args.OtherEntity);
    }

    protected override void OnDeployTry(Entity<RakeTrapComponent> ent, EntityUid user)
    {
        var xform = Transform(user);

        // The tile in front of the user (mime invisible-wall pattern).
        var offset = xform.LocalRotation.ToWorldVec();
        var coords = xform.Coordinates.Offset(offset).SnapToGrid(EntityManager);
        var tile = _turf.GetTileRef(coords);

        if (tile == null || TileBlockedFor(user, tile.Value))
        {
            _popup.PopupEntity(Loc.GetString("rake-deploy-blocked"), ent, user);
            return;
        }

        if (!_hands.TryDrop(user, ent, coords))
            return;

        _transform.SetCoordinates(ent, coords);

        ent.Comp.Armed = true;
        ent.Comp.Installer = user;
        Dirty(ent, ent.Comp);
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
        ent.Comp.Installer = null;
        Dirty(ent, ent.Comp);
        _audio.PlayPvs(ent.Comp.DisarmSound, ent);
        _popup.PopupEntity(Loc.GetString("rake-disarmed"), ent, args.User);
    }

    private void OnStepTriggered(Entity<RakeTrapComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!ent.Comp.Armed)
            return;

        // Only mobs step on rakes (furniture/items colliding must not disarm it)...
        if (!HasComp<MobStateComponent>(args.Tripper))
            return;

        // ...and the installer steps over their own trap freely.
        if (args.Tripper == ent.Comp.Installer)
            return;

        // Disarm immediately so nothing else can set it off.
        ent.Comp.Armed = false;
        ent.Comp.HitAt = _timing.CurTime;
        Dirty(ent, ent.Comp);

        Whack(ent.Owner, args.Tripper);
    }

    private void Whack(EntityUid rake, EntityUid victim)
    {
        if (TerminatingOrDeleted(rake) || !TryComp<RakeTrapComponent>(rake, out var comp))
            return;

        if (TerminatingOrDeleted(victim))
            return;

        // The rake snaps up and smacks: spring + impact sounds together.
        _audio.PlayPvs(comp.CockSound, rake);
        _audio.PlayPvs(comp.HitSound, rake);

        // Head hit.
        _damage.TryChangeDamage(victim, comp.Damage, origin: rake, targetPart: TargetBodyPart.Head);

        // Stunned (figuratively): down for a while, crawling, muffled audio.
        _stun.TryKnockdown(victim, comp.KnockdownTime, refresh: true, drop: true);

        var muffled = EnsureComp<RakeMuffledComponent>(victim);
        muffled.Gain = comp.MuffleGain;
        Timer.Spawn(comp.MuffleTime, () =>
        {
            if (!TerminatingOrDeleted(victim))
                RemComp<RakeMuffledComponent>(victim);
        });

        // Screen daze on the victim's client: impact flash + dizzy wobble, then tunnel vision.
        var dazed = EnsureComp<RakeDazedComponent>(victim);
        dazed.HitAt = _timing.CurTime;
        Timer.Spawn(dazed.Duration + dazed.TunnelTime, () =>
        {
            if (!TerminatingOrDeleted(victim))
                RemComp<RakeDazedComponent>(victim);
        });

        // Zoom punch: snap in, then let the eye lerp carry it back out.
        var eye = EnsureComp<ContentEyeComponent>(victim);
        var originalZoom = eye.TargetZoom;
        var punchZoom = originalZoom * 1.3f;
        _eye.SetZoom(victim, punchZoom, ignoreLimits: true);
        Timer.Spawn(TimeSpan.FromSeconds(0.15), () =>
        {
            if (TerminatingOrDeleted(victim) || !TryComp<ContentEyeComponent>(victim, out var e))
                return;

            // Don't clobber a zoom someone changed during the punch (e.g. a telescope).
            if (e.TargetZoom != punchZoom)
                return;

            _eye.SetZoom(victim, originalZoom, ignoreLimits: true);
        });

        RaiseLocalEvent(victim, new MoodEffectEvent(comp.Mood));
        _chat.TryEmoteWithChat(victim, "Scream", forceEmote: true);

        // Camera kick away from the rake — up in the face.
        var rakePos = _transform.GetWorldPosition(rake);
        var victimPos = _transform.GetWorldPosition(victim);
        var delta = victimPos - rakePos;
        var dir = delta.LengthSquared() > 0.01f ? delta.Normalized() : Vector2.UnitY;
        _recoil.KickCamera(victim, dir * 1.2f);

        _popup.PopupEntity(Loc.GetString("rake-hit"), victim, victim);
        comp.Installer = null;

        // Let the whack animation finish before the rake is shown lying down again.
        Timer.Spawn(comp.ResetVisualTime, () =>
        {
            if (TryComp<RakeTrapComponent>(rake, out var c))
            {
                c.HitAt = null;
                Dirty(rake, c);
            }
        });
    }

    /// <summary>
    ///     TurfSystem.IsTileBlocked, but the deploying user's own body doesn't block the tile
    ///     — standing on the tile edge used to block the deploy out of nowhere.
    /// </summary>
    private bool TileBlockedFor(EntityUid user, TileRef tile)
    {
        if (!TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return false;

        var xformQuery = GetEntityQuery<TransformComponent>();
        var (gridPos, gridRot, matrix) = _transform.GetWorldPositionRotationMatrix(Transform(tile.GridUid), xformQuery);

        var size = grid.TileSize;
        var localPos = new Vector2(tile.GridIndices.X * size + size / 2f, tile.GridIndices.Y * size + size / 2f);
        var worldPos = Vector2.Transform(localPos, matrix);

        // Scaled to 95% so it doesn't encompass walls on other tiles (as in TurfSystem).
        var tileAabb = Box2.UnitCentered.Scale(0.95f * size);
        var worldBox = new Box2Rotated(tileAabb.Translated(worldPos), gridRot, worldPos);
        tileAabb = tileAabb.Translated(localPos);

        var intersectionArea = 0f;
        var fixtureQuery = GetEntityQuery<FixturesComponent>();
        foreach (var ent in _lookup.GetEntitiesIntersecting(tile.GridUid, worldBox, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (ent == user)
                continue;

            if (!fixtureQuery.TryGetComponent(ent, out var fixtures))
                continue;

            var (pos, rot) = _transform.GetWorldPositionRotation(xformQuery.GetComponent(ent), xformQuery);
            rot -= gridRot;
            pos = (-gridRot).RotateVec(pos - gridPos);
            var entXform = new Transform(pos, (float)rot.Theta);

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                if ((fixture.CollisionLayer & (int)(CollisionGroup.Impassable | CollisionGroup.Opaque)) == 0)
                    continue;

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    var intersection = fixture.Shape.ComputeAABB(entXform, i).Intersect(tileAabb);
                    intersectionArea += intersection.Width * intersection.Height;
                    if (intersectionArea > 0.1f)
                        return true;
                }
            }
        }

        return false;
    }
}
