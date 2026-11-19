using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Audio;

/// <summary>
/// Sound transmission of this object as a percent of highs it blocks: 0 = transparent, 90 = 90% muffled.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SoundPathWeightComponent : Component
{
    /// <summary>Percent of highs blocked on transmission, 0-100.</summary>
    [DataField("muffle"), AutoNetworkedField]
    public float Muffle = 50f;
}
