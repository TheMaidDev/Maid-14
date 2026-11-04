namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Antagonist's own gear: leaves the round with them on extraction instead of staying on the station.
/// Added at runtime to starting gear, or in prototypes for items made by antag abilities.
/// </summary>
[RegisterComponent]
public sealed partial class ExtractionGearComponent : Component;
