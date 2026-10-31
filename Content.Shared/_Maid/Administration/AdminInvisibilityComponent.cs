using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Administration;

/// <summary>
///     Lets an admin ghost hide itself from regular ghosts. Only entities that see the
///     <c>VisibilityFlags.Admin</c> layer (other admin ghosts) can still see it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class AdminInvisibilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Invisible;

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleAdminInvisibility";

    [DataField]
    public EntityUid? ToggleActionEntity;

    /// <summary>
    ///     Sprite alpha multiplier while invisible, so the admin can tell the mode is on.
    /// </summary>
    [DataField]
    public float InvisibleAlphaMultiplier = 0.4f;

    /// <summary>
    ///     Client-side: sprite alpha before going invisible.
    /// </summary>
    [ViewVariables]
    public float? DefaultAlpha;
}

public sealed partial class ToggleAdminInvisibilityActionEvent : InstantActionEvent;
