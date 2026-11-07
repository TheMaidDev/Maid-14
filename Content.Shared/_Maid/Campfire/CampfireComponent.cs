using Content.Shared.Materials;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Maid.Campfire;

/// <summary>
/// A fire that must be fed with fuel and lit before it gives off heat.
/// While lit it burns fuel and heats items lying on top of it, so food can be cooked over it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CampfireComponent : Component
{
    /// <summary>
    /// Whether the fire is currently burning.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Lit;

    /// <summary>
    /// Coarse remaining burn time, shown on examine. The five non-empty levels are the fifths of
    /// <see cref="MaxFuel"/>, which the examine texts (written for 15 minutes) assume.
    /// </summary>
    [DataField, AutoNetworkedField]
    public CampfireFuelLevel FuelLevel;

    /// <summary>
    /// Remaining burn time in seconds.
    /// </summary>
    [DataField]
    public float Fuel;

    /// <summary>
    /// Maximum burn time in seconds that can be stored.
    /// </summary>
    [DataField]
    public float MaxFuel = 900f;

    /// <summary>
    /// Burn time in seconds granted per unit of material in the fuel item's physical composition.
    /// Only items made of these materials can be used as fuel.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> FuelPerMaterial = new()
    {
        ["Wood"] = 0.6f,
        ["Cloth"] = 0.3f,
        ["Paper"] = 0.2f,
    };

    /// <summary>
    /// Heat in joules per second delivered to each item lying on the fire.
    /// </summary>
    [DataField]
    public float HeatPower = 2400f;

    /// <summary>
    /// Items are not heated above this temperature (kelvin).
    /// </summary>
    [DataField]
    public float MaxItemTemperature = 520f;

    /// <summary>
    /// Burn time in seconds for items tagged with the key that have no material composition at all
    /// (paper, books and the like).
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<TagPrototype>, float> FuelTags = new()
    {
        ["Paper"] = 20f,
    };

    /// <summary>
    /// Items matching this are never accepted as fuel, even when made of a fuel material or tagged as fuel.
    /// Living creatures are never fuel either.
    /// </summary>
    [DataField]
    public EntityWhitelist? FuelBlacklist;

    /// <summary>
    /// Watts of heat put into the air of the fire's tile, by fuel stage: the fuel range is split into
    /// as many equal parts as there are entries, from nearly empty to full.
    /// </summary>
    [DataField]
    public List<float> AirHeatPowers = new() { 300f, 400f, 600f };

    /// <summary>
    /// The fire stops heating the air of its tile once it is this warm (kelvin).
    /// </summary>
    [DataField]
    public float AirHeatMaxTemperature = 313.15f;

    /// <summary>
    /// How far from the fire's center an item still counts as lying on it.
    /// </summary>
    [DataField]
    public float CookRange = 0.5f;

    /// <summary>
    /// Maximum number of items heated at the same time.
    /// </summary>
    [DataField]
    public int MaxCookedItems = 4;

    /// <summary>
    /// A lit fire goes out after standing in air with less oxygen than this (moles) for longer than
    /// <see cref="MaxOxygenlessTime"/>. Space and other airless tiles have none.
    /// </summary>
    [DataField]
    public float MinOxygenMoles = 1f;

    /// <summary>
    /// Seconds a lit fire may burn without oxygen before it goes out.
    /// </summary>
    [DataField]
    public float MaxOxygenlessTime = 3f;

    /// <summary>
    /// How long the fire has been without oxygen so far, in seconds.
    /// </summary>
    [ViewVariables]
    public float OxygenlessTime;

    /// <summary>
    /// Seconds between burn and heat ticks.
    /// </summary>
    [DataField]
    public float UpdateInterval = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;
}

[Serializable, NetSerializable]
public enum CampfireFuelLevel : byte
{
    Empty,
    Embers, // 1-3 minutes
    Low, // 4-7 minutes
    Medium, // 7-10 minutes
    High, // 10-12 minutes
    Full, // 13-15 minutes
}

[Serializable, NetSerializable]
public enum CampfireVisuals : byte
{
    Lit,
}
