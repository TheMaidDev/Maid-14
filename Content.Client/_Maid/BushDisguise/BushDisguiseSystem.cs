using Content.Shared._Maid.BushDisguise;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Client._Maid.BushDisguise;

public sealed class BushDisguiseSystem : SharedBushDisguiseSystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ISerializationManager _serMan = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BushDisguisedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BushDisguisedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BushDisguiseVisualComponent, AfterAutoHandleStateEvent>(OnVisualState);
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
        SpriteComponent? source = null;

        if (TryComp<SpriteComponent>(ent.Comp.Source, out var live))
            source = live;
        else if (ent.Comp.SourceProto is { } protoId
                 && _proto.TryIndex(protoId, out var proto)
                 && proto.TryGetComponent<SpriteComponent>(out var protoSprite, _factory))
            source = protoSprite;

        if (source == null)
            return;

        // Remove then re-add so no layers of the placeholder sprite are left behind.
        RemComp<SpriteComponent>(ent);
        var dest = AddComp<SpriteComponent>(ent);
        _serMan.CopyTo(source, ref dest, notNullableOverride: true);
    }
}
