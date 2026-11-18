using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Audio;

/// <summary>
/// Sound transmission weight of this object: 0 = transparent, higher = more muffling.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SoundPathWeightComponent : Component
{
    /// <summary>Transmission weight, 0 is fully transparent.</summary>
    [DataField, AutoNetworkedField]
    public float Weight = 1f;
}
