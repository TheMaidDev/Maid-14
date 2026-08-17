using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Trigger.Effects.SpawnUniqueItem;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SpawnUniqueItemOnTriggerComponent : BaseXOnTriggerComponent
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Proto = string.Empty;

    [DataField]
    public EntityUid? Previous = null;

    [DataField]
    public bool TryPutInHands = false;
}
