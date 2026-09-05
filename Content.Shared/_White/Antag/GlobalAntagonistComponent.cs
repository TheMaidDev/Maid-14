using Robust.Shared.Prototypes;

namespace Content.Shared._White.Antag;

[RegisterComponent]
public sealed partial class GlobalAntagonistComponent : Component
{
    [DataField(required: true)]
    public ProtoId<AntagonistPrototype>? AntagonistPrototype;
}
