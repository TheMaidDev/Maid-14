using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Radio;

[Prototype("channelSoundSpecifier")]
public sealed partial class ChannelSoundSpecifierPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public SoundSpecifier? Sound { get; private set; }
}
