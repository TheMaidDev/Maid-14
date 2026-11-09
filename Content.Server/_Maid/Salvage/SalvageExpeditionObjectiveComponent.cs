using Content.Server.Salvage;
using Content.Shared._Maid.Salvage;

namespace Content.Server._Maid.Salvage;

[RegisterComponent, Access(typeof(SalvageSystem), typeof(SpawnSalvageMissionJob))]
public sealed partial class SalvageExpeditionObjectiveComponent : Component
{
    [DataField]
    public SalvageMissionType MissionType;

    /// <summary>
    /// Structures or creatures that still have to be destroyed.
    /// </summary>
    [DataField]
    public List<EntityUid> Targets = new();
}
