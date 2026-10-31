using Robust.Shared.Audio;

namespace Content.Shared._Maid.Botany;

/// <summary>
///     Handheld scanner that shows a plant's traits, conditions and status.
///     Works on hydroponics trays, seed packets and produce. UI and sprite are taken from wizden,
///     the logic is written for maid's server-side botany (PlantHolderComponent / SeedData).
/// </summary>
[RegisterComponent]
public sealed partial class PlantAnalyzerComponent : Component
{
    [DataField]
    public TimeSpan ScanDelay = TimeSpan.FromSeconds(0.5);

    [DataField]
    public SoundSpecifier ScanningEndSound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");

    /// <summary>
    ///     How often the open UI is refreshed while the scanned plant grows.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public EntityUid? User;

    [ViewVariables]
    public TimeSpan NextUpdate;
}
