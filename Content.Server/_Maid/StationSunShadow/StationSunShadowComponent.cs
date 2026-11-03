using System.Numerics;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Gives every grid of this station a constant <see cref="Content.Shared.Light.Components.SunShadowComponent"/>,
/// imitating a star that always shines from one fixed side.
/// Values can be tweaked live by editing SunShadow on the grid via View Variables.
/// </summary>
[RegisterComponent]
public sealed partial class StationSunShadowComponent : Component
{
    /// <summary>
    /// Direction the shadows are cast in (opposite to the light source). Length is capped by <c>SunShadowComponent.MaxLength</c>.
    /// </summary>
    [DataField]
    public Vector2 Direction = new(0f, -4f);

    [DataField]
    public float Alpha = 0.5f;
}
