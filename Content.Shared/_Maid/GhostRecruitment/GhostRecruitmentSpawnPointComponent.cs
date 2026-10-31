using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Maid.GhostRecruitment;

[RegisterComponent]
public sealed partial class GhostRecruitmentSpawnPointComponent : Component
{
    [DataField("prototype", required: true)]
    public EntProtoId EntityPrototype = default!;

    [DataField("recruitmentName")]
    public string RecruitmentName = "default";

    [DataField("priority")] public int Priority = 5;

    [DataField]
    public string JobId = "Passenger";
}
