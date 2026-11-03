namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Runtime state for a station grid lit by <see cref="StationSunShadowComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class StationSunGridComponent : Component
{
    [ViewVariables]
    public EntityUid Station;

    /// <summary>
    /// Tiles currently left un-roofed so ambient (sun) light reaches them through windows.
    /// </summary>
    [ViewVariables]
    public HashSet<Vector2i> LitTiles = new();

    public TimeSpan NextUpdate;
}
