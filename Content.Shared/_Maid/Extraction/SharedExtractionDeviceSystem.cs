using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Keeps the extraction device closed for anyone but its owner.
/// </summary>
public sealed class SharedExtractionDeviceSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // using it in hand would fall back to activating it after a refused open, and warn twice
        SubscribeLocalEvent<ExtractionDeviceComponent, UseInHandEvent>(OnUseInHand, before: new[] { typeof(ActivatableUISystem) });
        SubscribeLocalEvent<ExtractionDeviceComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        // a client can send an open message directly, skipping the activation checks above
        SubscribeLocalEvent<ExtractionDeviceComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
    }

    private void OnMessageAttempt(Entity<ExtractionDeviceComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (ent.Comp.Owner != args.Actor)
            args.Cancel();
    }

    private void OnUseInHand(Entity<ExtractionDeviceComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Owner == args.User)
            return;

        args.Handled = true;
        Refuse(ent, args.User);
    }

    private void OnOpenAttempt(Entity<ExtractionDeviceComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.Owner == args.User)
            return;

        args.Cancel();
        Refuse(ent, args.User);
    }

    private void Refuse(Entity<ExtractionDeviceComponent> ent, EntityUid user)
    {
        _popup.PopupClient(Loc.GetString(ent.Comp.UnrecognizedMessage), ent, user);
    }
}
