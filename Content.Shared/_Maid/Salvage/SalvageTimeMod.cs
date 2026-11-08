using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Salvage;

/// <summary>
/// Expedition duration modifier. At least one prototype with zero cost is required.
/// </summary>
[Prototype("salvageTimeMod")]
public sealed partial class SalvageTimeMod : IPrototype, ISalvageMod
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("desc")]
    public LocId? Description { get; private set; }

    LocId ISalvageMod.Description => Description ?? string.Empty;

    /// <summary>
    /// Cost for difficulty modifiers.
    /// </summary>
    [DataField("cost")]
    public float Cost { get; private set; }

    /// <summary>
    /// Minimal mission duration in seconds.
    /// </summary>
    [DataField]
    public int MinDuration = 570;

    /// <summary>
    /// Maximal mission duration in seconds.
    /// </summary>
    [DataField]
    public int MaxDuration = 630;
}
