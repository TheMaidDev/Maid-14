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
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        // using it in hand would fall back to activating it after a refused open, and warn twice
        SubscribeLocalEvent<ExtractionDeviceComponent, UseInHandEvent>(OnUseInHand, before: new[] { typeof(ActivatableUISystem) });
        SubscribeLocalEvent<ExtractionDeviceComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        // a client can send an open message directly, skipping the activation checks above
        SubscribeLocalEvent<ExtractionDeviceComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
    }

    /// <summary>
    /// Only the owner, and only while they carry the device (hands, pockets, bags): a client can send
    /// UI messages to a device locked away elsewhere, the engine only closes the UI for range later in the tick.
    /// </summary>
    private void OnMessageAttempt(Entity<ExtractionDeviceComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (ent.Comp.DeviceOwner != args.Actor || !_transform.ContainsEntity(args.Actor, ent.Owner))
            args.Cancel();
    }

    private void OnUseInHand(Entity<ExtractionDeviceComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.DeviceOwner == args.User)
            return;

        args.Handled = true;
        Refuse(ent, args.User);
    }

    private void OnOpenAttempt(Entity<ExtractionDeviceComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.DeviceOwner == args.User)
            return;

        args.Cancel();
        Refuse(ent, args.User);
    }

    private void Refuse(Entity<ExtractionDeviceComponent> ent, EntityUid user)
    {
        _popup.PopupClient(Loc.GetString(ent.Comp.UnrecognizedMessage), ent, user);
    }
}
