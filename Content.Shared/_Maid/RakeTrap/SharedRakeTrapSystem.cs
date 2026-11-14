using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Timing;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     Shared logic for the rake trap: gates the step trigger on the armed state, hides it
///     from examine and turns clicks on an armed trap into disarm attempts (pickup is blocked).
/// </summary>
public abstract class SharedRakeTrapSystem : EntitySystem
{
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<RakeTrapComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<RakeTrapComponent, GettingPickedUpAttemptEvent>(OnGettingPickedUp);
        SubscribeLocalEvent<RakeTrapComponent, ExamineAttemptEvent>(OnExamineAttempt);
    }

    /// <summary>Only armed rakes can trigger, and anyone can trip over them — even whoever set them.</summary>
    private void OnStepTriggerAttempt(Entity<RakeTrapComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue |= ent.Comp.Armed;
    }

    /// <summary>The trap is hidden: no examine text for anyone.</summary>
    private void OnExamineAttempt(Entity<RakeTrapComponent> ent, ref ExamineAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    ///     Clicking an armed rake never picks it up — the click starts a disarm instead.
    ///     Picking an unarmed rake up works normally.
    /// </summary>
    private void OnGettingPickedUp(Entity<RakeTrapComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!ent.Comp.Armed)
            return;

        args.Cancel();
        OnArmedClicked(ent, args.User);
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
