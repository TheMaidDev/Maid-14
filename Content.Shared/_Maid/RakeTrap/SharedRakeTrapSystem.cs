using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Verbs;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     Shared logic for the rake trap: gates the step trigger on the armed state, hides the
///     deployed trap from examine and hover outline, turns clicks on an armed trap into disarm
///     attempts (pickup is blocked), and deploys the held rake via a right-click verb.
/// </summary>
public abstract class SharedRakeTrapSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<RakeTrapComponent, InteractHandEvent>(OnInteractHand, before: [typeof(SharedItemSystem)]);
        SubscribeLocalEvent<RakeTrapComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<RakeTrapComponent, GettingPickedUpAttemptEvent>(OnGettingPickedUp);
        SubscribeLocalEvent<RakeTrapComponent, ExamineAttemptEvent>(OnExamineAttempt);
    }

    /// <summary>
    ///     Only armed rakes can trigger, and only humanoids can set them off — furniture,
    ///     items or other mobs must not. Installer immunity is checked server-side only.
    /// </summary>
    private void OnStepTriggerAttempt(Entity<RakeTrapComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue |= ent.Comp.Armed && IsTriggerTarget(args.Tripper);
    }

    /// <summary>
    ///     Whether this entity can set the trap off at all: any humanoid
    ///     (HumanoidAppearanceComponent — humans, IPCs and the other species).
    ///     Mice, pets and borgs must not.
    /// </summary>
    protected bool IsTriggerTarget(EntityUid uid)
    {
        return HasComp<HumanoidAppearanceComponent>(uid);
    }

    /// <summary>
    ///     Deploying the rake is a right-click verb on the held item: use-in-hand belongs to
    ///     wielding (the rake is also a weapon). The act runs server-side via ExecuteVerb.
    /// </summary>
    private void OnGetVerbs(Entity<RakeTrapComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Hands == null || !args.CanAccess || !args.CanInteract)
            return;

        if (!_hands.IsHolding((args.User, args.Hands), ent, out _))
            return;

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("rake-deploy-verb"),
            Act = () => OnDeployTry(ent, user),
        });
    }

    /// <summary>The deployed trap is hidden: no examine for anyone. The item itself examines normally.</summary>
    private void OnExamineAttempt(Entity<RakeTrapComponent> ent, ref ExamineAttemptEvent args)
    {
        if (ent.Comp.Armed)
            args.Cancel();
    }

    /// <summary>
    ///     Clicking an armed rake starts a disarm instead of a pickup. Intercepted before
    ///     SharedItemSystem so the pickup is never attempted (no client prediction rollback).
    /// </summary>
    private void OnInteractHand(Entity<RakeTrapComponent> ent, ref InteractHandEvent args)
    {
        if (!ent.Comp.Armed)
            return;

        args.Handled = true;

        if (IsMindIgnored(args.User))
            return;

        OnArmedClicked(ent, args.User);
    }

    /// <summary>
    ///     Backstop for pickup routes that bypass the click (storage, verbs): an armed rake
    ///     can never be picked up. Picking an unarmed rake up works normally.
    /// </summary>
    private void OnGettingPickedUp(Entity<RakeTrapComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!ent.Comp.Armed)
            return;

        args.Cancel();
    }

    /// <summary>Ghosts and observers can neither deploy nor disarm the trap (wizard-traps pattern).</summary>
    private bool IsMindIgnored(EntityUid user)
    {
        return HasComp<GhostComponent>(user) || HasComp<SpectralComponent>(user) || !HasComp<MobStateComponent>(user);
    }

    /// <summary>Server: place the rake, armed, on the tile in front of the user.</summary>
    protected virtual void OnDeployTry(Entity<RakeTrapComponent> ent, EntityUid user) { }

    /// <summary>Server: start the disarm doafter on an armed rake.</summary>
    protected virtual void OnArmedClicked(Entity<RakeTrapComponent> ent, EntityUid user) { }
}
