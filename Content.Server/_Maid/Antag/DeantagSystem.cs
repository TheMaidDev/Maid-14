using Content.Server.Administration.Logs;
using Content.Shared._Maid.Antag;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.Antag;

/// <summary>
///     Removes antagonist status (mind roles and the data-driven leftovers from <see cref="DeantagPrototype"/>) from a mind.
/// </summary>
public sealed class DeantagSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    /// <summary>
    ///     Resolves a mind from either a mind entity or an entity that has a mind.
    /// </summary>
    public bool TryGetMind(EntityUid target, out Entity<MindComponent> mind)
    {
        mind = default;

        if (TryComp<MindComponent>(target, out var mindComp))
        {
            mind = (target, mindComp);
            return true;
        }

        if (_mind.GetMind(target) is not { } mindId || !TryComp(mindId, out mindComp))
            return false;

        mind = (mindId, mindComp);
        return true;
    }

    /// <summary>
    ///     Removes antag mind roles from the mind.
    /// </summary>
    /// <param name="mind">The mind to clean.</param>
    /// <param name="antagId">Antag prototype id to remove, or null to remove every antag.</param>
    /// <returns>Ids of the antag prototypes whose roles were removed.</returns>
    public List<string> Deantag(Entity<MindComponent> mind, string? antagId = null)
    {
        DeantagPrototype? specific = null;
        if (antagId != null && !_proto.TryIndex(antagId, out specific))
            Log.Warning($"No deantag prototype '{antagId}', only its mind roles will be removed.");

        // Collect first, removing roles mutates MindRoles.
        var roles = new Dictionary<EntProtoId<MindRoleComponent>, string?>();
        foreach (var role in mind.Comp.MindRoles)
        {
            if (!TryComp<MindRoleComponent>(role, out var roleComp))
                continue;

            if (antagId == null ? !roleComp.Antag : roleComp.AntagPrototype != antagId)
                continue;

            if (MetaData(role).EntityPrototype?.ID is { } roleProto)
                roles[roleProto] = roleComp.AntagPrototype;
        }

        var removed = new List<string>();
        foreach (var (roleProto, roleAntag) in roles)
        {
            if (!_role.MindRemoveRole(mind.AsNullable(), roleProto))
                continue;

            if (roleAntag != null && !removed.Contains(roleAntag))
                removed.Add(roleAntag);
        }

        var cleanup = new List<DeantagPrototype>();
        if (specific != null)
        {
            cleanup.Add(specific);
        }
        else
        {
            foreach (var id in removed)
            {
                if (_proto.TryIndex<DeantagPrototype>(id, out var proto))
                    cleanup.Add(proto);
            }
        }

        foreach (var proto in cleanup)
        {
            if (mind.Comp.OwnedEntity is { } body)
                EntityManager.RemoveComponents(body, proto.RemoveComponents);

            EntityManager.RemoveComponents(mind.Owner, proto.RemoveMindComponents);
        }

        _adminLog.Add(LogType.Mind,
            LogImpact.High,
            $"Deantag ({antagId ?? "all"}) applied to {ToPrettyString(mind.Comp.OwnedEntity)}, removed antags: {string.Join(", ", removed)}");

        return removed;
    }
}
