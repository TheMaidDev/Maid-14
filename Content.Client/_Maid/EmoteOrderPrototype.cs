using Robust.Shared.Prototypes;

namespace Content.Client._Maid;

[Prototype("emoteOrder")]
public sealed partial class EmoteOrderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int Order = 0;
}
