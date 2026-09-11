using Content.Server.Preferences.Managers;
using Content.Shared._Maid.Utils;
using Content.Shared._RMC14.GhostColor;
using Content.Shared._White.CustomGhostSystem;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._White.CustomGhosts;

public sealed class CustomGhostSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CustomGhostComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnPlayerAttached(Entity<CustomGhostComponent> ent, ref PlayerAttachedEvent args)
    {
        var player = args.Player;
        var prefs = _prefs.GetPreferences(player.UserId);
        var ghostProtoId = prefs.CustomGhost;

        if (!_prototypeManager.TryIndex(ghostProtoId, out var customGhost) || !customGhost.CanUse(player))
        {
            ghostProtoId = "default";
            if (!_prototypeManager.TryIndex(ghostProtoId, out customGhost))
                return;
        }

        SetupCustomGhost(ent.Owner, customGhost, ent.Comp);
    }

    public void SetupCustomGhost(EntityUid uid, CustomGhostPrototype customGhost, CustomGhostComponent component)
    {
        component.Ghost = customGhost.ID;
        Dirty(uid, component);
        EntityManager.MergeComponents(uid, customGhost.AddComponents, _serManager);
        // _meta.SetEntityName(uid, customGhost.DisplayName); // We actually don't want that i think?
        // _meta.SetEntityDescription(uid, customGhost.DisplayDesc);
    }
}
