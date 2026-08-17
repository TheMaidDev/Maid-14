using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared.Mind;
using Content.Shared.Roles;
using System;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

[DataDefinition]
public sealed partial class AdaptiveScoreHasComponentInChildrenCondition : AdaptiveScoreTargetedCondition
{
    [DataField(required: true)]
    public string Component { get; set; } = string.Empty;

    protected override bool ConditionMetOnTarget(EntityUid? ent, IEntityManager entMan)
    {
        if (ent == null)
            return false;

        var compFactory = IoCManager.Resolve<IComponentFactory>();
        if (!compFactory.TryGetRegistration(Component, out var registration))
            return false;

        var xformQuery = entMan.GetEntityQuery<TransformComponent>();
        return HasComponent(ent.Value, registration.Type, entMan, xformQuery);
    }

    private static bool HasComponent(EntityUid uid, Type componentType, IEntityManager entMan, EntityQuery<TransformComponent> xformQuery)
    {
        if (!xformQuery.TryGetComponent(uid, out var xform))
            return false;

        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entMan.HasComponent(child, componentType))
                return true;

            if (HasComponent(child, componentType, entMan, xformQuery))
                return true;
        }

        return false;
    }
}
