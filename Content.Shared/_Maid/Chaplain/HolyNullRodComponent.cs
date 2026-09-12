using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Chaplain;

[RegisterComponent]
public sealed partial class HolyNullRodComponent : Component
{
    [DataField]
    public List<EntProtoId> Weapons = new();
}
