using Content.Shared.Atmos;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Botany;

[Serializable, NetSerializable]
public enum PlantAnalyzerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed partial class PlantAnalyzerDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Seed characteristics shown in the traits section.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlantAnalyzerTraits
{
    public float Endurance;
    public int Yield;
    public float Lifespan;
    public float Maturation;
    public float Production;
    public float Potency;
    public int GrowthStages;
}

/// <summary>
///     Live tray data. Only present when a hydroponics tray is scanned.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlantAnalyzerHolderStatus
{
    public bool Dead;
    public bool ReadyForHarvest;
    public float Health;
    public int Age;
    public int GrowthStage;
    public float WaterLevel;
    public float NutritionLevel;
    public float MutationLevel;
    public float MutationMod;
}

/// <summary>
///     Everything the plant analyzer window shows. Botany is server-only in maid, so the server
///     gathers all of it instead of the client reading plant components like in wizden.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlantAnalyzerState : BoundUserInterfaceState
{
    public NetEntity Target;

    /// <summary>
    ///     Plant (or seed) name, already localized.
    /// </summary>
    public string Name = string.Empty;

    /// <summary>
    ///     False for an empty tray: only the tray status and warnings are shown then.
    /// </summary>
    public bool HasSeed;

    public PlantAnalyzerTraits? Traits;

    /// <summary>
    ///     Traits of the unmodified seed prototype, for the +/- deltas. Null if it couldn't be found.
    /// </summary>
    public PlantAnalyzerTraits? PrototypeTraits;

    /// <summary>
    ///     <c>NoRepeat</c>, <c>Repeat</c> or <c>SelfHarvest</c>.
    /// </summary>
    public string HarvestType = string.Empty;

    public PlantAnalyzerHolderStatus? Holder;

    public float IdealHeat;
    public float HeatTolerance;
    public float LowPressureTolerance;
    public float HighPressureTolerance;
    public float WaterConsumption;
    public float NutrientConsumption;

    public Dictionary<Gas, float> ConsumedGases = new();
    public Dictionary<Gas, float> ExudedGases = new();

    /// <summary>
    ///     Reagent prototype id -> amount at harvest for the current potency.
    /// </summary>
    public Dictionary<string, float> Chemicals = new();

    /// <summary>
    ///     Localized names of notable mutations (seedless, unviable, ligneous...).
    /// </summary>
    public List<string> Mutations = new();

    /// <summary>
    ///     Localized markup lines, same as on examine.
    /// </summary>
    public List<string> Warnings = new();
}
