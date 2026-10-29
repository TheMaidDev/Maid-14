using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.OfferItem;

/// <summary>
///     Lets an entity hand the item in its active hand to another entity with this component.
///     The offerer presses the offer key and clicks the target; the target accepts via the alert.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(OfferItemSystem))]
public sealed partial class OfferItemComponent : Component
{
    /// <summary>
    ///     The offerer is choosing who to give the item to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsInOfferMode;

    /// <summary>
    ///     Someone is offering an item to this entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsInReceiveMode;

    /// <summary>
    ///     The hand holding the offered item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Hand;

    /// <summary>
    ///     The offered item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Item;

    /// <summary>
    ///     The other side of the offer: the receiver for the offerer and vice versa.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField]
    public float MaxOfferDistance = 2f;

    [DataField]
    public ProtoId<AlertPrototype> OfferAlert = "Offer";
}
