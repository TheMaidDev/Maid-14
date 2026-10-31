using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._Maid.OfferItem;

/// <summary>
///     Handles giving items from hand to hand: offer key → click the receiver → receiver clicks the alert.
/// </summary>
public sealed class OfferItemSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OfferItemComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OfferItemComponent, AcceptOfferAlertEvent>(OnAcceptOfferAlert);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OfferItem, InputCmdHandler.FromDelegate(HandleOfferKey, handle: false, outsidePrediction: false))
            .Register<OfferItemSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<OfferItemSystem>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<OfferItemComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // The offered item left the hand.
            if (comp.Hand != null && _hands.GetHeldItem(uid, comp.Hand) != comp.Item)
            {
                Reset((uid, comp));
                continue;
            }

            if (comp.Target is { } target && !_transform.InRange(uid, target, comp.MaxOfferDistance))
                Reset((uid, comp));
        }
    }

    public bool IsInOfferMode(EntityUid? uid)
    {
        return TryComp<OfferItemComponent>(uid, out var comp) && comp.IsInOfferMode;
    }

    private void HandleOfferKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (!TryComp<OfferItemComponent>(uid, out var comp))
            return;

        // A second press cancels whatever is going on: choosing a target, waiting for the receiver or a pending offer to us.
        if (comp.IsInOfferMode || comp.Target != null)
        {
            if (comp.IsInOfferMode && comp.Target == null)
                _popup.PopupClient(Loc.GetString("offer-item-cancel"), uid, uid);

            Reset((uid, comp));
            return;
        }

        if (!_actionBlocker.CanInteract(uid, null))
            return;

        var hand = _hands.GetActiveHand(uid);
        if (hand == null || _hands.GetHeldItem(uid, hand) is not { } item)
        {
            _popup.PopupClient(Loc.GetString("offer-item-empty-hand"), uid, uid);
            return;
        }

        comp.IsInOfferMode = true;
        comp.Hand = hand;
        comp.Item = item;
        Dirty(uid, comp);

        _popup.PopupClient(Loc.GetString("offer-item-select-target", ("item", Identity.Entity(item, EntityManager))), uid, uid);
    }

    private void OnInteractUsing(Entity<OfferItemComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || args.User == ent.Owner)
            return;

        if (!TryComp<OfferItemComponent>(args.User, out var offerer) || !offerer.IsInOfferMode || offerer.Item is not { } item)
            return;

        // The receiver is busy with another offer.
        if (ent.Comp.IsInOfferMode || ent.Comp.Target != null)
            return;

        args.Handled = true;

        offerer.IsInOfferMode = false;
        offerer.Target = ent;
        Dirty(args.User, offerer);

        ent.Comp.IsInReceiveMode = true;
        ent.Comp.Target = args.User;
        Dirty(ent);

        _alerts.ShowAlert(ent, ent.Comp.OfferAlert);

        _popup.PopupClient(Loc.GetString("offer-item-try-give",
                ("item", Identity.Entity(item, EntityManager)),
                ("target", Identity.Entity(ent, EntityManager))),
            args.User,
            args.User);

        if (_net.IsServer)
        {
            _popup.PopupEntity(Loc.GetString("offer-item-try-give-target",
                    ("user", Identity.Entity(args.User, EntityManager)),
                    ("item", Identity.Entity(item, EntityManager))),
                args.User,
                ent);
        }
    }

    private void OnAcceptOfferAlert(Entity<OfferItemComponent> ent, ref AcceptOfferAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Receive(ent.AsNullable());
    }

    /// <summary>
    ///     Accepts the pending offer and moves the item into the receiver's hands.
    /// </summary>
    public void Receive(Entity<OfferItemComponent?> receiver)
    {
        if (!Resolve(receiver, ref receiver.Comp, false) || !receiver.Comp.IsInReceiveMode)
            return;

        if (receiver.Comp.Target is not { } offererUid ||
            !TryComp<OfferItemComponent>(offererUid, out var offerer) ||
            offerer.Item is not { } item)
        {
            Reset((receiver, receiver.Comp), false);
            return;
        }

        if (!_actionBlocker.CanInteract(receiver, null))
            return;

        if (!_hands.TryPickup(receiver, item))
        {
            _popup.PopupClient(Loc.GetString("offer-item-full-hand"), receiver, receiver);
            return;
        }

        if (_net.IsServer)
        {
            _popup.PopupEntity(Loc.GetString("offer-item-give",
                    ("item", Identity.Entity(item, EntityManager)),
                    ("target", Identity.Entity(receiver, EntityManager))),
                offererUid,
                offererUid);
            _popup.PopupEntity(Loc.GetString("offer-item-give-other",
                    ("user", Identity.Entity(offererUid, EntityManager)),
                    ("item", Identity.Entity(item, EntityManager)),
                    ("target", Identity.Entity(receiver, EntityManager))),
                offererUid,
                Filter.PvsExcept(offererUid, entityManager: EntityManager).RemovePlayerByAttachedEntity(receiver),
                true);
        }

        _popup.PopupClient(Loc.GetString("offer-item-give-target",
                ("user", Identity.Entity(offererUid, EntityManager)),
                ("item", Identity.Entity(item, EntityManager))),
            receiver,
            receiver);

        Reset((receiver, receiver.Comp), false);
    }

    /// <summary>
    ///     Cancels the offer for both the given entity and its counterpart.
    /// </summary>
    private void Reset(Entity<OfferItemComponent> ent, bool popup = true)
    {
        if (ent.Comp.Target is { } otherUid && TryComp<OfferItemComponent>(otherUid, out var other))
        {
            var (offerer, receiver) = ent.Comp.IsInReceiveMode ? (otherUid, ent.Owner) : (ent.Owner, otherUid);
            var item = ent.Comp.IsInReceiveMode ? other.Item : ent.Comp.Item;

            if (popup && _net.IsServer && item != null)
            {
                _popup.PopupEntity(Loc.GetString("offer-item-no-give",
                        ("item", Identity.Entity(item.Value, EntityManager)),
                        ("target", Identity.Entity(receiver, EntityManager))),
                    offerer,
                    offerer);
                _popup.PopupEntity(Loc.GetString("offer-item-no-give-target",
                        ("user", Identity.Entity(offerer, EntityManager)),
                        ("item", Identity.Entity(item.Value, EntityManager))),
                    offerer,
                    receiver);
            }

            if (other.Target == ent.Owner)
                Clear((otherUid, other));
        }

        Clear(ent);
    }

    private void Clear(Entity<OfferItemComponent> ent)
    {
        if (ent.Comp.IsInReceiveMode)
            _alerts.ClearAlert(ent, ent.Comp.OfferAlert);

        ent.Comp.IsInOfferMode = false;
        ent.Comp.IsInReceiveMode = false;
        ent.Comp.Hand = null;
        ent.Comp.Item = null;
        ent.Comp.Target = null;
        Dirty(ent);
    }
}

/// <summary>
///     Raised when the receiver clicks the offer alert.
/// </summary>
public sealed partial class AcceptOfferAlertEvent : BaseAlertEvent;
