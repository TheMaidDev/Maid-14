using Content.Shared.Ghost;
using Content.Shared._White.CustomGhostSystem;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._White.CustomGhosts;

public sealed class CustomGhostSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        // We need that stuff since we adding client-side only components like "sprite". That should work properly
        SubscribeLocalEvent<CustomGhostComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    private void OnAfterHandleState(Entity<CustomGhostComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.Ghost is not {} ghostProto || ghostProto == "default")
            return;

        if (!_prototypeManager.TryIndex(ghostProto, out var customGhost))
            return;

        // MAID custom ghosts: AddComponents replaces Sprite instead of merging it, so keep what GhostSystem set on the old one
        var wasVisible = !TryComp<SpriteComponent>(ent, out var oldSprite) || oldSprite.Visible;

        _entityManager.AddComponents(ent.Owner, customGhost.AddComponents, true);

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _sprite.SetVisible((ent.Owner, sprite), wasVisible);

        if (TryComp<GhostComponent>(ent, out var ghost))
            _sprite.LayerSetColor((ent.Owner, sprite), 0, ghost.Color);
    }
}
