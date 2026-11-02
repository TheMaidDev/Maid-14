using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Tools;

/// <summary>
/// Makes a welding sparks effect glow (using its light color) and flicker, fading in when it appears and out when it is released.
/// The glow is derived from the timestamps, so the light's own values are never stored or restored.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeldingGlowComponent : Component
{
    /// <summary>
    /// Peak light energy. The flicker moves between a fraction of this and the full value.
    /// </summary>
    [DataField]
    public float Energy = 4f;

    /// <summary>
    /// Light radius.
    /// </summary>
    [DataField]
    public float Radius = 3f;

    /// <summary>
    /// Seconds to go from no glow to full glow when the effect appears, and back after it is released.
    /// </summary>
    [DataField]
    public float FadeTime = 0.7f;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan StartTime;

    /// <summary>
    /// Set when welding ended. The effect is deleted once the fade out finishes.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? EndTime;
}
