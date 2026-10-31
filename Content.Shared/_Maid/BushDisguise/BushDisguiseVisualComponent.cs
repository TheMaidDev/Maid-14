using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.BushDisguise;

/// <summary>
/// The entity drawn in place of a disguised player.
/// The client copies its sprite from <see cref="Source"/> (or <see cref="SourceProto"/> as a fallback).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class BushDisguiseVisualComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Source;

    [DataField, AutoNetworkedField]
    public EntProtoId? SourceProto;
}
