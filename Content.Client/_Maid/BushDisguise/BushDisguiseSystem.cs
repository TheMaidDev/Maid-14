using System.Linq;
using Content.Shared._Maid.BushDisguise;
using Robust.Client.GameObjects;

namespace Content.Client._Maid.BushDisguise;

public sealed class BushDisguiseSystem : SharedBushDisguiseSystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    /// <summary>
    /// Visuals still waiting for the plant they copy to show up on the client.
    /// </summary>
    private readonly HashSet<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BushDisguisedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BushDisguisedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BushDisguiseVisualComponent, AfterAutoHandleStateEvent>(OnVisualState);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_pending.Count > 0)
            _pending.RemoveWhere(TryCopySprite);
    }

    private void OnStartup(Entity<BushDisguisedComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        ent.Comp.WasVisible = sprite.Visible;
        _sprite.SetVisible((ent, sprite), false);
    }

    private void OnShutdown(Entity<BushDisguisedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetVisible((ent, sprite), ent.Comp.WasVisible);
    }

    private void OnVisualState(Entity<BushDisguiseVisualComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryCopySprite(ent))
            _pending.Add(ent);
    }

    /// <summary>
    /// Copies the look of the plant onto the visual's own sprite.
    /// Returns true when there is nothing more to do, false if the plant isn't ready yet.
    /// </summary>
    private bool TryCopySprite(EntityUid uid)
    {
        if (!TryComp<BushDisguiseVisualComponent>(uid, out var visual) || !TryComp<SpriteComponent>(uid, out var dest))
            return true;

        if (!TryComp<SpriteComponent>(visual.Source, out var source) || source.AllLayers.FirstOrDefault() is not { } layer)
            return false;

        _sprite.LayerSetRsi((uid, dest), 0, layer.Rsi, layer.RsiState);
        _sprite.SetDrawDepth((uid, dest), source.DrawDepth);
        return true;
    }
}
