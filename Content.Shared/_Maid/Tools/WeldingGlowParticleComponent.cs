using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Tools;

/// <summary>
/// A light attached to a welding user that flickers while they weld, fading in when it appears and out when welding ends.
/// The glow is derived from the timestamps, so the light's own values are never stored or restored.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeldingGlowParticleComponent : Component
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
    /// Set when welding ended. The entity is deleted once the fade out finishes.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? EndTime;

    /// <summary>
    /// The welding user this light is attached to. Server only.
    /// </summary>
    [ViewVariables]
    public EntityUid User;

    /// <summary>
    /// Index of the user's welding DoAfter. Server only.
    /// </summary>
    [ViewVariables]
    public ushort DoAfterIdx;
}
