using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Weapons.Melee.CritSystem;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class MaidCritComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public float CritChance = 0.25f;

    [DataField(required: true), AutoNetworkedField]
    public float CritMultiplier = 2.5f;

    [DataField(required: true), AutoNetworkedField]
    public ProtoId<DamageTypePrototype> CritType = "Slash";
}
