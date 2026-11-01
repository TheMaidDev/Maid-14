using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.BushDisguise;

/// <summary>
/// Marks a potted plant that an empty-handed player can pick up (after a do-after)
/// to become that plant: the player is hidden and the plant is shown instead.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BushDisguiseComponent : Component
{
    /// <summary>
    /// How long the user has to stand still to pick the plant up.
    /// </summary>
    [DataField]
    public TimeSpan PickupDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Entity spawned on the user that is drawn instead of them.
    /// </summary>
    [DataField]
    public EntProtoId VisualPrototype = "MaidBushDisguiseVisual";

    /// <summary>
    /// How far from the user the plant can be put down. Throwing is unaffected.
    /// </summary>
    [DataField]
    public float MaxDropDistance = 0.7f;

    /// <summary>
    /// Set only while the do-after is finishing, so that the pickup is not blocked.
    /// </summary>
    [ViewVariables]
    public bool PickupAllowed;
}
