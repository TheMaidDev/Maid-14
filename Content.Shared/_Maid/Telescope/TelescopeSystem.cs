using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Telescope;

/// <summary>
///     Applies the <see cref="TelescopeComponent.Offset"/> to the holder's eye.
///     The offset itself is calculated on the client and reported with <see cref="TelescopeOffsetMessage"/>.
/// </summary>
public sealed class TelescopeSystem : EntitySystem
{
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TelescopeComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<TelescopeComponent, HeldRelayedEvent<GetEyeOffsetRelayedEvent>>(OnGetHeldEyeOffset);
        SubscribeLocalEvent<TelescopeComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<TelescopeComponent, HandDeselectedEvent>(OnHandDeselected);
        SubscribeLocalEvent<TelescopeComponent, ComponentShutdown>(OnShutdown);

        SubscribeNetworkEvent<TelescopeOffsetMessage>(OnOffsetMessage);
    }

    /// <summary>
    ///     Returns the telescope the user is looking through: the item in the active hand, or the user itself.
    /// </summary>
    public Entity<TelescopeComponent>? GetTelescope(EntityUid? user)
    {
        if (user == null)
            return null;

        if (_hands.GetActiveItem(user.Value) is { } held && TryComp<TelescopeComponent>(held, out var heldTelescope))
            return (held, heldTelescope);

        if (TryComp<TelescopeComponent>(user, out var telescope))
            return (user.Value, telescope);

        return null;
    }

    /// <summary>
    ///     Sets the offset and immediately updates the user's eye.
    /// </summary>
    public void SetOffset(Entity<TelescopeComponent> telescope, EntityUid user, Vector2 offset)
    {
        // The offset may come from a client: NaN would slip past the length check below and infinity would turn into NaN.
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            offset = Vector2.Zero;

        var maxOffset = 1f / MathF.Max(telescope.Comp.Divisor, 0.01f);
        if (offset.LengthSquared() > maxOffset * maxOffset)
            offset = Vector2.Normalize(offset) * maxOffset;

        if (telescope.Comp.Offset == offset && telescope.Comp.LastHolder == user)
            return;

        telescope.Comp.Offset = offset;
        telescope.Comp.LastHolder = user;
        UpdateEye(user);
    }

    private void OnGetEyeOffset(Entity<TelescopeComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.Offset;
    }

    private void OnGetHeldEyeOffset(Entity<TelescopeComponent> ent, ref HeldRelayedEvent<GetEyeOffsetRelayedEvent> args)
    {
        // Only the item in the active hand is looked through.
        if (ent.Comp.LastHolder is not { } holder || _hands.GetActiveItem(holder) != ent.Owner)
            return;

        args.Args.Offset += ent.Comp.Offset;
    }

    private void OnUnequipped(Entity<TelescopeComponent> ent, ref GotUnequippedHandEvent args)
    {
        Reset(ent, args.User);
    }

    private void OnHandDeselected(Entity<TelescopeComponent> ent, ref HandDeselectedEvent args)
    {
        Reset(ent, args.User);
    }

    private void OnShutdown(Entity<TelescopeComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.LastHolder is { } holder && !TerminatingOrDeleted(holder))
            Reset(ent, holder);
    }

    private void Reset(Entity<TelescopeComponent> ent, EntityUid user)
    {
        ent.Comp.Offset = Vector2.Zero;
        ent.Comp.LastHolder = null;
        UpdateEye(user);
    }

    private void UpdateEye(EntityUid user)
    {
        if (TryComp<EyeComponent>(user, out var eye))
            _contentEye.UpdateEyeOffset((user, eye));
    }

    private void OnOffsetMessage(TelescopeOffsetMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        if (GetTelescope(user) is not { } telescope)
            return;

        SetOffset(telescope, user, msg.Offset);
    }
}

/// <summary>
///     Sent by the client with its current telescope offset, so the server eye (and PVS) follows the camera.
/// </summary>
[Serializable, NetSerializable]
public sealed class TelescopeOffsetMessage(Vector2 offset) : EntityEventArgs
{
    public Vector2 Offset = offset;
}
