using Robust.Shared.GameStates;

namespace Content.Shared._Maid.BushDisguise;

/// <summary>
/// Added to a player that is carrying a <see cref="BushDisguiseComponent"/> plant.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BushDisguisedComponent : Component
{
    /// <summary>
    /// The plant held in the user's hands.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Bush;

    /// <summary>
    /// The entity drawn instead of the user.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Visual;

    /// <summary>
    /// Client-side only: sprite visibility to restore when the disguise ends.
    /// </summary>
    [ViewVariables]
    public bool WasVisible = true;
}
