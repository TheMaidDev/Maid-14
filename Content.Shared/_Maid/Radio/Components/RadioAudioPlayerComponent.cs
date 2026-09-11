using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

namespace Content.Shared._Maid.Radio.Components;

[RegisterComponent]
public sealed partial class RadioAudioPlayerComponent : Component
{
    [DataField]
    public RadioSoundTarget Target = RadioSoundTarget.Self;

    [DataField]
    public bool PlayGlobally = false;

    [DataField]
    public SoundSpecifier? OverrideSound = null;
}

[Serializable, NetSerializable]
public enum RadioSoundTarget : byte
{
    Self,
    Parent,
}
