using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Handheld device of an extractable antagonist: shows the extraction status and holds its controls.
/// Only its owner can open it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExtractionDeviceComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;

    /// <summary>
    /// Popup for anyone else trying to open it.
    /// </summary>
    [DataField]
    public LocId UnrecognizedMessage = "extraction-device-unrecognized";

    /// <summary>
    /// Shortest time between two UI updates. Building one counts objective progress, so spamming
    /// the buttons must not rebuild it every message. Requests in between are answered later.
    /// </summary>
    [DataField]
    public TimeSpan UiUpdateCooldown = TimeSpan.FromSeconds(0.5);

    [ViewVariables]
    public TimeSpan NextUiUpdate;

    [ViewVariables]
    public bool UiUpdatePending;
}
