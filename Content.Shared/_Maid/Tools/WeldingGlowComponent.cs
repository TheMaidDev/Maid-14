using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Tools;

/// <summary>
/// Put on a welder. While it welds something, a <see cref="WeldingGlowParticleComponent"/> light is attached to the user.
/// </summary>
[RegisterComponent]
public sealed partial class WeldingGlowComponent : Component
{
    /// <summary>
    /// Light entity to attach to the welding user. Should have <see cref="WeldingGlowParticleComponent"/>.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Prototype;
}
