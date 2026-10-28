using Robust.Shared.Utility;

namespace Content.Shared._Maid.Lighting;

/// <summary>
///     Draws a glow mask over the entity while its point light is enabled.
///     Toggled client-side by <c>MaidCVars.EnableLightsGlowing</c>.
/// </summary>
[RegisterComponent]
public sealed partial class LightGlowComponent : Component
{
    /// <summary>
    ///     Forces the glow on or off. If null, follows the point light state.
    /// </summary>
    [DataField]
    public bool? Enabled;

    [DataField]
    public SpriteSpecifier Sprite = new SpriteSpecifier.Texture(new ResPath("/Textures/_Maid/Effects/LightMasks/lightmask_lamp.png"));

    /// <summary>
    ///     Offset of the mask relative to the entity, in tiles.
    /// </summary>
    [DataField]
    public System.Numerics.Vector2 Offset = new(-0.5f, 0.5f);

    /// <summary>
    ///     Glow color. If null, the point light color is used.
    /// </summary>
    [DataField]
    public Color? Color;
}
