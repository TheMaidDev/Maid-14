using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     Shared logic for the rake trap: gates the step trigger on the armed state, hides it
///     from examine and turns clicks on an armed trap into disarm attempts (pickup is blocked).
/// </summary>
public abstract class SharedRakeTrapSystem : EntitySystem
{
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<RakeTrapComponent, InteractHandEvent>(OnInteractHand, before: [typeof(SharedItemSystem)]);
        SubscribeLocalEvent<RakeTrapComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<RakeTrapComponent, GettingPickedUpAttemptEvent>(OnGettingPickedUp);
        SubscribeLocalEvent<RakeTrapComponent, ExamineAttemptEvent>(OnExamineAttempt);
    }

    /// <summary>
    ///     Only armed rakes can trigger, and only MobHuman-likes can set them off — furniture,
    ///     items or other mobs must not. Installer immunity is checked server-side only.
    /// </summary>
    private void OnStepTriggerAttempt(Entity<RakeTrapComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue |= ent.Comp.Armed && IsTriggerTarget(ent.Comp, args.Tripper);
    }

    /// <summary>
    ///     Whether this entity can set the trap off at all: the configured trigger prototype
    ///     and everything inheriting from it (any mob when no prototype is configured).
    /// </summary>
    protected bool IsTriggerTarget(RakeTrapComponent comp, EntityUid uid)
    {
        if (comp.TriggerPrototype is not { } baseId)
            return HasComp<MobStateComponent>(uid);

        if (!TryComp<MetaDataComponent>(uid, out var meta) || meta.EntityPrototype is not { } proto)
            return false;

        return PrototypeInheritsFrom(proto, baseId);
    }

    /// <summary>Walks the (possibly multi-parent) prototype inheritance chain up to the base prototype.</summary>
    private bool PrototypeInheritsFrom(EntityPrototype proto, string baseId)
    {
        if (proto.ID == baseId)
            return true;

        if (proto.Parents == null)
            return false;

        foreach (var parent in proto.Parents)
        {
            if (_proto.TryIndex<EntityPrototype>(parent, out var parentProto) && PrototypeInheritsFrom(parentProto, baseId))
                return true;
        }

        return false;
    }

    /// <summary>The trap is hidden: no examine text for anyone.</summary>
    private void OnExamineAttempt(Entity<RakeTrapComponent> ent, ref ExamineAttemptEvent args)
    {
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

    /// <summary>Using the rake in hand deploys it, armed, on the tile in front of the user.</summary>
    private void OnUseInHand(Entity<RakeTrapComponent> ent, ref UseInHandEvent args)
    {
        if (IsMindIgnored(args.User))
            return;

        if (!_useDelay.TryResetDelay(ent.Owner, checkDelayed: true))
            return;

        args.Handled = true;
        OnDeployTry(ent, args.User);
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
