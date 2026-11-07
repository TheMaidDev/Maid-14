using Content.Shared.Materials;
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
    /// Coarse fuel level, used for examine text and visuals.
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
    Low,
    Medium,
    High,
}

[Serializable, NetSerializable]
public enum CampfireVisuals : byte
{
    Lit,
}
