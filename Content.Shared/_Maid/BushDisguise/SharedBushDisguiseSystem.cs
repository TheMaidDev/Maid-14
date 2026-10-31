using System.Linq;
using Content.Goobstation.Common.Identity;
using Content.Goobstation.Common.Speech;
using Content.Shared._Shitcode.Heretic.Components;
using Content.Shared.Chat;
using Content.Shared.Coordinates;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Shared._Maid.BushDisguise;

/// <summary>
/// Lets an empty-handed player pick up a potted plant and become it.
/// The plant itself is held as a two-handed item (see <see cref="MultiHandedItemComponent"/>), the player's sprite is
/// hidden by the client and a <see cref="BushDisguiseVisualComponent"/> entity is drawn instead.
/// Dropping the plant for any reason ends the disguise.
/// </summary>
public abstract class SharedBushDisguiseSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedIdentitySystem _identity = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly SpriteSpecifier.Texture PickupIcon = new(new("/Textures/Interface/VerbIcons/pickup.svg.192dpi.png"));

    public override void Initialize()
    {
        base.Initialize();

        // The plant
        SubscribeLocalEvent<BushDisguiseComponent, InteractHandEvent>(OnInteractHand,
            before: [typeof(SharedItemSystem)], after: [typeof(SecretStashSystem)]); // let the stash hand out its item first
        SubscribeLocalEvent<BushDisguiseComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs, after: [typeof(SharedItemSystem)]);
        SubscribeLocalEvent<BushDisguiseComponent, GettingPickedUpAttemptEvent>(OnGettingPickedUp);
        SubscribeLocalEvent<BushDisguiseComponent, BushPickupDoAfterEvent>(OnPickupDoAfter);
        SubscribeLocalEvent<BushDisguiseComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<BushDisguiseComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<BushDisguiseComponent, ComponentShutdown>(OnBushShutdown);

        // The disguised player
        SubscribeLocalEvent<BushDisguisedComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName);
        SubscribeLocalEvent<BushDisguisedComponent, TryGetIdentityShortInfoEvent>(OnGetIdentityShortInfo);
        SubscribeLocalEvent<BushDisguisedComponent, GetIdentityRepresentationEntityEvent>(OnGetIdentityEntity);
        SubscribeLocalEvent<BushDisguisedComponent, GetSpeechSoundEvent>(OnGetSpeechSound);
        SubscribeLocalEvent<BushDisguisedComponent, GetEmoteSoundsEvent>(OnGetEmoteSounds);
        SubscribeLocalEvent<BushDisguisedComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<BushDisguisedComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    #region Picking up

    private void OnInteractHand(Entity<BushDisguiseComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartPickup(ent, args.User);
    }

    private void OnGetVerbs(Entity<BushDisguiseComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        // The regular item pickup would be blocked, so swap it for one that starts the do-after.
        var pickupText = Loc.GetString("pick-up-verb-get-data-text");
        var pickupInventoryText = Loc.GetString("pick-up-verb-get-data-text-inventory");
        args.Verbs.RemoveWhere(verb => verb.Text == pickupText || verb.Text == pickupInventoryText);

        if (args.Hands == null || !args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = pickupText,
            Icon = PickupIcon,
            Act = () => TryStartPickup(ent, user),
        });
    }

    private void OnGettingPickedUp(Entity<BushDisguiseComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!ent.Comp.PickupAllowed)
            args.Cancel();
    }

    public bool TryStartPickup(Entity<BushDisguiseComponent> ent, EntityUid user)
    {
        if (HasComp<BushDisguisedComponent>(user)
            || !HasComp<HandsComponent>(user)
            || _container.IsEntityInContainer(ent))
            return false;

        if (_hands.EnumerateHeld(user).Any())
        {
            _popup.PopupClient(Loc.GetString("bush-disguise-hands-not-empty", ("bush", ent.Owner)), ent, user);
            return false;
        }

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.PickupDelay, new BushPickupDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnPickupDoAfter(Entity<BushDisguiseComponent> ent, ref BushPickupDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        // Someone else may have grabbed it, or it may have been shut away, while we were busy.
        if (_container.IsEntityInContainer(ent))
            return;

        if (HasComp<BushDisguisedComponent>(args.User) || _hands.EnumerateHeld(args.User).Any())
        {
            _popup.PopupEntity(Loc.GetString("bush-disguise-hands-not-empty", ("bush", ent.Owner)), ent, args.User);
            return;
        }

        ent.Comp.PickupAllowed = true;
        _hands.TryPickup(args.User, ent, null, animateUser: false);
        ent.Comp.PickupAllowed = false;
    }

    #endregion

    #region Disguising

    private void OnGotEquippedHand(Entity<BushDisguiseComponent> ent, ref GotEquippedHandEvent args)
    {
        if (_net.IsClient)
            return;

        Disguise(args.User, ent);
    }

    private void OnGotUnequippedHand(Entity<BushDisguiseComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (_net.IsClient)
            return;

        Undisguise(args.User, ent);
    }

    private void OnBushShutdown(Entity<BushDisguiseComponent> ent, ref ComponentShutdown args)
    {
        // The plant got deleted while being held.
        if (_net.IsClient)
            return;

        var holder = Transform(ent).ParentUid;
        if (holder.IsValid() && !TerminatingOrDeleted(holder))
            Undisguise(holder, ent);
    }

    private void Disguise(EntityUid user, Entity<BushDisguiseComponent> bush)
    {
        if (HasComp<BushDisguisedComponent>(user))
            return;

        var meta = MetaData(bush);

        var visual = SpawnAttachedTo(bush.Comp.VisualPrototype, user.ToCoordinates());
        _meta.SetEntityName(visual, meta.EntityName);
        _meta.SetEntityDescription(visual, meta.EntityDescription);

        var visualComp = EnsureComp<BushDisguiseVisualComponent>(visual);
        visualComp.Source = bush;
        visualComp.SourceProto = meta.EntityPrototype?.ID;
        Dirty(visual, visualComp);

        // Hits and pulls aimed at the visual land on the real player.
        var relay = EnsureComp<TargetInteractionRelayComponent>(visual);
        relay.RelayEntity = user;
        Dirty(visual, relay);

        var disguised = EnsureComp<BushDisguisedComponent>(user);
        disguised.Bush = bush;
        disguised.Visual = visual;
        Dirty(user, disguised);

        _identity.QueueIdentityUpdate(user);
    }

    private void Undisguise(EntityUid user, EntityUid bush)
    {
        if (!TryComp<BushDisguisedComponent>(user, out var disguised) || disguised.Bush != bush)
            return;

        if (disguised.Visual is { } visual)
            QueueDel(visual);

        RemComp<BushDisguisedComponent>(user);
        _identity.QueueIdentityUpdate(user);
    }

    private void OnMobStateChanged(Entity<BushDisguisedComponent> ent, ref MobStateChangedEvent args)
    {
        if (_net.IsClient || args.NewMobState == MobState.Alive)
            return;

        _hands.TryDrop(ent.Owner, ent.Comp.Bush, checkActionBlocker: false);
    }

    #endregion

    #region Identity

    private void OnTransformSpeakerName(Entity<BushDisguisedComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (TerminatingOrDeleted(ent.Comp.Bush))
            return;

        args.VoiceName = Name(ent.Comp.Bush);

        if (TryComp<SpeechComponent>(ent.Comp.Bush, out var speech))
            args.SpeechVerb = speech.SpeechVerb;
    }

    private void OnGetIdentityShortInfo(Entity<BushDisguisedComponent> ent, ref TryGetIdentityShortInfoEvent args)
    {
        if (args.RequestForAccessLogging || TerminatingOrDeleted(ent.Comp.Bush))
            return;

        args.Title = Name(ent.Comp.Bush);
    }

    private void OnGetIdentityEntity(Entity<BushDisguisedComponent> ent, ref GetIdentityRepresentationEntityEvent args)
    {
        if (TerminatingOrDeleted(ent.Comp.Bush))
            return;

        args.Uid = ent.Comp.Bush;
    }

    private void OnGetSpeechSound(Entity<BushDisguisedComponent> ent, ref GetSpeechSoundEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.SpeechSoundProtoId = CompOrNull<SpeechComponent>(ent.Comp.Bush)?.SpeechSounds;
    }

    private void OnGetEmoteSounds(Entity<BushDisguisedComponent> ent, ref GetEmoteSoundsEvent args)
    {
        // Plants don't laugh or scream.
        if (args.Handled)
            return;

        args.Handled = true;
        args.EmoteSoundProtoId = null;
    }

    #endregion

    private void OnAttackAttempt(Entity<BushDisguisedComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }
}
