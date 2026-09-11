using Content.Shared._RMC14.GhostColor;
using Content.Shared._White.CustomGhostSystem;
using Content.Shared._Maid.Utils;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Client._White.CustomGhosts;

public sealed class CustomGhostSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();

        // We need that stuff since we adding client-side only components like "sprite". That should work properly
        SubscribeLocalEvent<CustomGhostComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    private void OnAfterHandleState(Entity<CustomGhostComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.Ghost == null)
            return;

        if (!_prototypeManager.TryIndex(ent.Comp.Ghost.Value, out var customGhost))
            return;

        EntityManager.MergeComponents(ent.Owner, customGhost.AddComponents, _serManager);
    }
}
