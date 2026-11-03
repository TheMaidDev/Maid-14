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
    /// Direction the shadows are cast in (opposite to the light source). Length is capped by <c>SunShadowComponent.MaxLength</c>.
    /// </summary>
    [DataField]
    public Vector2 Direction = new(0f, -5f);

    [DataField]
    public float Alpha = 0.5f;

    /// <summary>
    /// Ambient light color applied to the map the station grid is on, so open space around the station is lit by the star.
    /// Grids are implicitly roofed, so the station interior is not affected by it. Editable via VV on the map's MapLight.
    /// </summary>
    [DataField]
    public Color SunColor = Color.FromHex("#B8B8C0");

    /// <summary>
    /// If true, sunlight passes through windows that face open space and lights this many tiles of the station behind them.
    /// </summary>
    [DataField]
    public bool LightThroughWindows = true;

    [DataField]
    public int WindowLightDepth = 2;

    /// <summary>
    /// Unlit gaps up to this many tiles wide between two lit tiles (across the light direction) get filled,
    /// so neighbouring window beams merge into one instead of looking like separate stripes.
    /// </summary>
    [DataField]
    public int WindowLightGapFill = 0;
}
