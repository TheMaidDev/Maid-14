using System.Linq;
using Content.Shared._Maid.BushDisguise;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Maid.BushDisguise;

public sealed class BushDisguiseSystem : SharedBushDisguiseSystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    /// <summary>
    /// Visuals still looking for the plant sprite they should copy.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingVisuals = new();

    /// <summary>
    /// Visuals that already show a plant and are safe to be seen.
    /// </summary>
    private readonly HashSet<EntityUid> _readyVisuals = new();

    /// <summary>
    /// Disguised players whose own sprite is waiting for their visual to be ready.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingHolders = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BushDisguisedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BushDisguisedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BushDisguiseVisualComponent, AfterAutoHandleStateEvent>(OnVisualState);
        SubscribeLocalEvent<BushDisguiseVisualComponent, ComponentShutdown>(OnVisualShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_pendingVisuals.Count > 0)
            _pendingVisuals.RemoveWhere(TryCopySprite);

        if (_pendingHolders.Count > 0)
            _pendingHolders.RemoveWhere(TryHide);
    }

    private void OnStartup(Entity<BushDisguisedComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            ent.Comp.WasVisible = sprite.Visible;

        // Hide the player together with the plant showing up, never before.
        if (!TryHide(ent.Owner))
            _pendingHolders.Add(ent.Owner);
    }

    private void OnShutdown(Entity<BushDisguisedComponent> ent, ref ComponentShutdown args)
    {
        _pendingHolders.Remove(ent.Owner);

        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetVisible((ent, sprite), ent.Comp.WasVisible);
    }

    private void OnVisualState(Entity<BushDisguiseVisualComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryCopySprite(ent.Owner))
            _pendingVisuals.Add(ent.Owner);
    }

    private void OnVisualShutdown(Entity<BushDisguiseVisualComponent> ent, ref ComponentShutdown args)
    {
        _pendingVisuals.Remove(ent.Owner);
        _readyVisuals.Remove(ent.Owner);
    }

    /// <summary>
    /// Hides the player once their visual is ready. Returns true when there is nothing more to do.
    /// </summary>
    private bool TryHide(EntityUid uid)
    {
        if (!TryComp<BushDisguisedComponent>(uid, out var disguised) || !TryComp<SpriteComponent>(uid, out var sprite))
            return true;

        if (disguised.Visual is not { } visual || !_readyVisuals.Contains(visual))
            return false;

        _sprite.SetVisible((uid, sprite), false);
        return true;
    }

    /// <summary>
    /// Copies the look of the plant onto the visual's own sprite and shows it.
    /// Returns true when the live plant was used, false while we are still waiting for it.
    /// </summary>
    private bool TryCopySprite(EntityUid uid)
    {
        if (!TryComp<BushDisguiseVisualComponent>(uid, out var visual) || !TryComp<SpriteComponent>(uid, out var dest))
            return true;

        var live = true;
        SpriteComponent? source = null;
        ISpriteLayer? layer = null;

        if (TryComp<SpriteComponent>(visual.Source, out var plantSprite))
        {
            source = plantSprite;
            layer = plantSprite.AllLayers.FirstOrDefault();
        }

        // The plant may not have reached us yet, show its prototype sprite meanwhile.
        if (layer == null
            && visual.SourceProto is { } protoId
            && _proto.TryIndex(protoId, out var proto)
            && proto.TryGetComponent<SpriteComponent>(out var protoSprite, _factory))
        {
            live = false;
            source = protoSprite;
            layer = protoSprite.AllLayers.FirstOrDefault();
        }

        if (source == null || layer == null)
            return false;

        _sprite.LayerSetRsi((uid, dest), 0, layer.Rsi, layer.RsiState);
        _sprite.SetDrawDepth((uid, dest), source.DrawDepth);
        _sprite.SetVisible((uid, dest), true);
        _readyVisuals.Add(uid);

        return live;
    }
}
