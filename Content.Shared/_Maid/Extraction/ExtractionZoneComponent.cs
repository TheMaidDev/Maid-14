using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Extraction zone marker. Only <see cref="Extractee"/> and admins receive it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExtractionZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Extractee;

    /// <summary>
    /// Half-size of the square zone in tiles, 1 makes a 3x3 square.
    /// </summary>
    [DataField]
    public int Range = 1;

    /// <summary>
    /// Entity the zone was placed at, skipped when the zone is re-rolled.
    /// </summary>
    [DataField]
    public EntityUid? Anchor;

    /// <summary>
    /// Placed at a random tile because the station had no anchors: messages give exact coordinates.
    /// </summary>
    [DataField]
    public bool ShowCoordinates;
}
