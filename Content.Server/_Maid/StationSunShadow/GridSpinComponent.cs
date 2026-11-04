namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Debug helper: keeps rotating the grid around its centre. Added and removed with the <c>sunspin</c> command.
/// </summary>
[RegisterComponent]
public sealed partial class GridSpinComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DegreesPerSecond;
}
