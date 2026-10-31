using Content.Server.Preferences.Managers;
using Content.Server.Database; // MAID custom ghosts
using Content.Shared._RMC14.GhostColor;
using Content.Shared._White.CustomGhostSystem;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._White.CustomGhosts;

public sealed class CustomGhostSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!; // MAID custom ghosts

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CustomGhostComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

// MAID BEGIN custom ghosts
    private void OnPlayerAttached(Entity<CustomGhostComponent> ent, ref PlayerAttachedEvent args)
    {
        if (ent.Comp.Ghost != null)
            return;

        var player = args.Player;

        SetupGhost(ent.Owner, player);
    }

    private async void SetupGhost(EntityUid uid, ICommonSession player)
    {
        try
        {
            if (!TryComp<CustomGhostComponent>(uid, out var component))
                return;

            if (!_userDb.IsLoadComplete(player))
            {
                try { await _userDb.WaitLoadComplete(player); }
                catch (OperationCanceledException) { return; } // Probably?
            }
            // Are we still there
            if (TerminatingOrDeleted(uid) || !TryComp<CustomGhostComponent>(uid, out component))
                return;
            // Same
            if (player.AttachedEntity != uid)
                return;

            // That can return false if database unloads moment when its loaded
            if (!_prefs.TryGetCachedPreferences(player.UserId, out var prefs))
                return;

            var ghostProtoId = prefs.CustomGhost;

            // Do that again for funnies
            if (!TryComp(uid, out CustomGhostComponent? comp))
                return;

            if (!_prototypeManager.TryIndex(ghostProtoId, out var customGhost) || !customGhost.CanUse(player))
            {
                ghostProtoId = "default";
                if (!_prototypeManager.TryIndex(ghostProtoId, out customGhost))
                    return;
            }

            if (ghostProtoId == "default")
                return; // They don't do anything, so should be fine

            component.Ghost = customGhost.ID;
            Dirty(uid, component);

            EntityManager.AddComponents(uid, customGhost.AddComponents);

            _meta.SetEntityName(uid, $"{customGhost.DisplayName} ({Name(uid)})");
            _meta.SetEntityDescription(uid, customGhost.DisplayDesc);
        }
        catch (Exception e)
        {
            // Silently ignore
            // We can't properly return error
            // But atleast it will not crash or bug
        }
    }
}
