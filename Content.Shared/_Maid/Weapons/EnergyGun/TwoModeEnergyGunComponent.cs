using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Weapons.EnergyGun;

/// <summary>
/// Energy gun that switches fire modes in hand, like the e-gun of the old WhiteDream build.
/// The gun always shows the same amount of charges, a stronger mode just spends more of them per shot.
/// Works with <see cref="ProjectileBatteryAmmoProviderComponent"/>, its fireCost is the battery cost of one charge.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedTwoModeEnergyGunSystem))]
public sealed partial class TwoModeEnergyGunComponent : Component
{
    [DataField(required: true)]
    public List<TwoModeEnergyGunMode> Modes = new();

    [DataField, AutoNetworkedField]
    public int CurrentMode;

    [DataField]
    public SoundSpecifier? ToggleSound = new SoundPathSpecifier("/Audio/_Maid/Weapons/Guns/Misc/egun_toggle.ogg");

    /// <summary>
    /// How many charge indicator states the sprite has, without the empty one.
    /// </summary>
    [DataField]
    public int MagSteps = 5;
}

[DataDefinition]
public sealed partial class TwoModeEnergyGunMode
{
    [DataField(required: true)]
    public EntProtoId Proto;

    /// <summary>
    /// How many charges one shot spends.
    /// </summary>
    [DataField]
    public int Charges = 1;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public SoundSpecifier? SoundGunshot;

    /// <summary>
    /// In-hand sprite prefix while this mode is selected.
    /// </summary>
    [DataField]
    public string? HeldPrefix;

    /// <summary>
    /// Charge indicator states are "{MagState}-{step}".
    /// </summary>
    [DataField]
    public string MagState = "mag";
}

[Serializable, NetSerializable]
public enum TwoModeEnergyGunVisuals : byte
{
    Mode,
}

[Serializable, NetSerializable]
public enum TwoModeEnergyGunVisualLayers : byte
{
    Mag,
}
