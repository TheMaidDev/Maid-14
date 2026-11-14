using Content.Shared._Maid.RakeTrap;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Maid.RakeTrap;

/// <summary>
///     Shows <see cref="RakeHitOverlay"/> while the local player has <see cref="RakeDazedComponent"/>
///     (stepped on a rake). Standard local-player overlay pattern (DrunkSystem / DeafnessSystem).
/// </summary>
public sealed class RakeDazeSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private RakeHitOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeDazedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<RakeDazedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RakeDazedComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RakeDazedComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnInit(Entity<RakeDazedComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity == ent)
            TryAdd();
    }

    private void OnShutdown(Entity<RakeDazedComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent)
            TryRemove();
    }

    private void OnAttached(Entity<RakeDazedComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        TryAdd();
    }

    private void OnDetached(Entity<RakeDazedComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        TryRemove();
    }

    private void TryAdd()
    {
        if (_overlay != null)
            return;

        _overlay = new RakeHitOverlay();
        _overlayMan.AddOverlay(_overlay);
    }

    private void TryRemove()
    {
        if (_overlay == null)
            return;

        _overlayMan.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
