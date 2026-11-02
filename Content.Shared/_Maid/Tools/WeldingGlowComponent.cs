namespace Content.Shared._Maid.Tools;

/// <summary>
/// Makes a lit welder with a point light glow white and flicker while it is welding something.
/// Visual only, driven on the client.
/// </summary>
[RegisterComponent]
public sealed partial class WeldingGlowComponent : Component
{
    /// <summary>
    /// Peak light energy while welding. The flicker moves between a fraction of this and the full value.
    /// </summary>
    [DataField]
    public float Energy = 4f;

    /// <summary>
    /// Light radius while fully blended into the welding glow.
    /// </summary>
    [DataField]
    public float Radius = 3f;

    /// <summary>
    /// Seconds to go from no glow to full glow when welding starts, and back when it stops.
    /// </summary>
    [DataField]
    public float FadeTime = 0.7f;

    /// <summary>
    /// 0 is the normal welder light, 1 is the full flickering glow.
    /// </summary>
    [ViewVariables]
    public float Blend;

    /// <summary>
    /// Whether the original light values below are captured and the glow is applied.
    /// </summary>
    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public Color OriginalColor;

    [ViewVariables]
    public float OriginalEnergy;

    [ViewVariables]
    public float OriginalRadius;
}
