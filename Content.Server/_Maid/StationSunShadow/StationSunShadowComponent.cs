using System.Numerics;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Gives every grid of this station a constant <see cref="Content.Shared.Light.Components.SunShadowComponent"/>
/// and lights up the map it is on,
/// imitating a star that always shines from one fixed side.
/// Values can be tweaked live by editing SunShadow on the grid via View Variables.
/// </summary>
[RegisterComponent]
public sealed partial class StationSunShadowComponent : Component
{
    /// <summary>
    /// Direction the shadows are cast in (opposite to the light source). Its length is how far the station's shadow reaches, in tiles.
    /// </summary>
    [DataField]
    public Vector2 Direction = new(0f, -40f);

    [DataField]
    public float Alpha = 1f;

    /// <summary>
    /// Ambient light color applied to the map the station grid is on, so open space around the station is lit by the star.
    /// Grids are implicitly roofed, so the station interior is not affected by it. Editable via VV on the map's MapLight.
    /// </summary>
    [DataField]
    public Color SunColor = Color.FromHex("#B8B8C0");
}
