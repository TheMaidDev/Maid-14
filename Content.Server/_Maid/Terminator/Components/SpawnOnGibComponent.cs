using Robust.Shared.Prototypes;

namespace Content.Server._Maid.Terminator.Components;

[RegisterComponent]
public sealed partial class SpawnOnGibComponent : Component
{
    [DataField] public EntProtoId? Prototype;
    [DataField] public bool TransferMind = false;
}