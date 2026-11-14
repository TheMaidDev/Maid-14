using Content.Shared.CCVar;
using Content.Shared._Maid.RakeTrap;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._Maid.RakeTrap;

/// <summary>
///     Ducks the local client's master gain while the local player has
///     <see cref="RakeMuffledComponent"/> (stepped on a rake). Pattern from Goob's DeafnessSystem.
/// </summary>
public sealed class RakeMuffleSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IAudioManager _audio = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private float _originalVolume;
    private bool _ducked;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeMuffledComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RakeMuffledComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RakeMuffledComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RakeMuffledComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnStartup(Entity<RakeMuffledComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent)
            Duck(ent.Comp.Gain);
    }

    private void OnShutdown(Entity<RakeMuffledComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent)
            Restore();
    }

    private void OnAttached(Entity<RakeMuffledComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        Duck(ent.Comp.Gain);
    }

    private void OnDetached(Entity<RakeMuffledComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        Restore();
    }

    private void Duck(float gain)
    {
        _originalVolume = _cfg.GetCVar(CCVars.AudioMasterVolume);
        _audio.SetMasterGain(gain);
        _ducked = true;
    }

    private void Restore()
    {
        if (!_ducked)
            return;

        _ducked = false;
        _audio.SetMasterGain(_originalVolume);
    }
}
