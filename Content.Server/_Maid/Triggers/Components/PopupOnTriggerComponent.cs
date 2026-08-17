using Content.Shared.Popups;

namespace Content.Server._Maid.Triggers.Components;

/// <summary>
/// Displays a popup on a target entity when this entity is triggered.
/// </summary>
[RegisterComponent]
public sealed partial class PopupOnTriggerComponent : Component
{
    [DataField(required: true)]
    public string Popup = string.Empty;

    [DataField]
    public PopupType PopupType = PopupType.MediumCaution;
}
