using System.Diagnostics;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Shared.Ghost;
using Content.Shared.Radio.Components;
using Content.Shared._Maid.Radio;
using Content.Shared._Maid.Radio.Components;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.Radio.EntitySystems;

public sealed class RadioSoundSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private static readonly ProtoId<ChannelSoundSpecifierPrototype> DefaultSpecifierPrototype = "Default";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioAudioPlayerComponent, RadioReceiveEvent>(OnRadioReceive);
    }

    private void OnRadioReceive(EntityUid uid, RadioAudioPlayerComponent component, ref RadioReceiveEvent args)
    {
        var sound = component.OverrideSound ?? GetChannelSound(args.Channel);

        var target = GetTarget(uid, component.Target);

        if (component.PlayGlobally)
            _audio.PlayPvs(sound, target);
        else
            _audio.PlayGlobal(sound, target);

    }

    private EntityUid GetTarget(EntityUid from, RadioSoundTarget target) => target switch
    {
        RadioSoundTarget.Self => from,
        RadioSoundTarget.Parent => Transform(from).ParentUid,
        _ => throw new UnreachableException(),
    };

    private SoundSpecifier? GetChannelSound(ProtoId<RadioChannelPrototype> channel)
    {
        if (_prototypeManager.TryIndex<ChannelSoundSpecifierPrototype>(channel, out var specifier))
            return specifier.Sound;

        if (_prototypeManager.TryIndex(DefaultSpecifierPrototype, out var defaultSpecifier))
            return defaultSpecifier.Sound;

        return null;
    }
}
