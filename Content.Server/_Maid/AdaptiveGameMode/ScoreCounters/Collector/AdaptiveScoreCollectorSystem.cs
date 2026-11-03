using System.Linq;
using System.Collections;
using System.Reflection;
using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;
using Content.Server._Maid.Extraction;
using Content.Shared._Maid.Extraction;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Collector;

public sealed class AdaptiveScoreCollectorSystem : EntitySystem, IAdaptiveBalanceInfoProvider
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetAdaptiveScoreEvent>(OnGetAdaptiveScore);
    }

    private IEnumerable<EntityUid> GetEntities() =>
        _entityManager.GetEntities();

    private IEnumerable<EntityUid> GetEntities(Type componentType) =>
        _entityManager.GetAllComponents(componentType).Select(e => e.Uid);

    private void OnGetAdaptiveScore(ref GetAdaptiveScoreEvent ev)
    {
        var query = EntityQueryEnumerator<AdaptiveScoreCollectorComponent>();

        while (query.MoveNext(out var uid, out var collector))
        {
            var entities = collector.EnumerateComponent is not null
                           && _componentFactory.TryGetRegistration(collector.EnumerateComponent, out var reg)
                ? GetEntities(reg.Type)
                : GetEntities();

            foreach (var ent in entities)
            {
                if (IsConditionsMet(collector.Conditions, ent))
                    ev.Add(ent, collector.ChaosScore, collector.CombatScore);
            }
        }
    }


    public bool IsConditionsMet(IEnumerable<AdaptiveScoreCondition> conditions, EntityUid ent)
    {
        EntityUid? mob = null;
        Entity<MindComponent>? mind = null;

        if (TryComp<MindRoleComponent>(ent, out var mindRole))
        {
            var mindId = mindRole.Mind.Owner;
            if (TryComp<MindComponent>(mindId, out var mindComp))
            {
                mob = mindComp.OwnedEntity;
                mind = new Entity<MindComponent>(mindId, mindComp);
            }
        }
        else if (TryComp<MindComponent>(ent, out var mindComp))
        {
            mob = mindComp.OwnedEntity;
            mind = new Entity<MindComponent>(ent, mindComp);
        }
        else
        {
            var mindSystem = _entityManager.System<SharedMindSystem>();
            if (mindSystem.TryGetMind(ent, out var mobMindId, out var mobMindComp))
            {
                mob = ent;
                mind = new Entity<MindComponent>(mobMindId, mobMindComp);
            }
            else
            {
                mob = ent;
            }
        }

        // characters that left the round through extraction are kept alive on a paused map, they don't count
        if (IsExtracted(ent, mob, mind))
            return false;

        return conditions.All(condition => condition.ConditionMet(ent, mob, mind, _entityManager));
    }

    private bool IsExtracted(EntityUid ent, EntityUid? mob, Entity<MindComponent>? mind)
    {
        if (mind is { } m && HasComp<ExtractedMindComponent>(m))
            return true;

        return IsExtractedBody(ent) || mob is { } body && IsExtractedBody(body);
    }

    private bool IsExtractedBody(EntityUid uid)
    {
        return TryComp<AntagExtractableComponent>(uid, out var extractable) && extractable.Extracted;
    }
#if DEBUG
    public IEnumerable<AdaptiveBalanceInfo> GetBalanceInfo()
    {
        var rawResults = GetRawResults(_protoManager);
        if (rawResults == null)
            yield break;

        foreach (var (protoId, mapping) in rawResults)
        {
            var compMapping = GetComponentMapping(mapping, "AdaptiveScoreCollector");
            if (compMapping is null)
                continue;

            var component = _serializationManager.Read<AdaptiveScoreCollectorComponent?>(compMapping);
            if (component is null)
                continue;

            yield return new AdaptiveBalanceInfo
            {
                Entity = protoId,
                Condition = string.Join(
                    " + ",
                    new[] { component.EnumerateComponent ?? "" }
                        .Concat(
                            component.Conditions
                                .Select(cond => cond.BalanceTableName)
                        )
                        .Where(s => !string.IsNullOrEmpty(s))
                ),
                ChaosFrom = component.ChaosScore,
                CombatFrom = component.CombatScore,
            };
        }
    }

    private static Dictionary<string, MappingDataNode>? GetRawResults(IPrototypeManager protoManager)
    {
        if (protoManager is not PrototypeManager prototypeManager)
            return null;

        // Some reflection nonsense to retrieve private fields. May break on engine update
        var kindsField = typeof(PrototypeManager)
            .GetField("_kinds", BindingFlags.Instance | BindingFlags.NonPublic);

        if (kindsField?.GetValue(prototypeManager) is not IDictionary dict)
            return null;

        if (!dict.Contains(typeof(EntityPrototype)))
            return null;

        var kindData = dict[typeof(EntityPrototype)];

        var rawResultsField = kindData?
            .GetType()
            .GetField("RawResults", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return rawResultsField?.GetValue(kindData) as Dictionary<string, MappingDataNode>;
    }

    private static MappingDataNode? GetComponentMapping(MappingDataNode mapping, string componentName)
    {
        if (!mapping.TryGetValue("components", out var componentsNode) || componentsNode is not SequenceDataNode sequenceNode)
            return null;

        foreach (var node in sequenceNode)
        {
            if (node is not MappingDataNode compMapping)
                continue;

            if (!compMapping.TryGetValue("type", out var typeNode) || typeNode is not ValueDataNode valNode)
                continue;

            if (valNode.Value == componentName)
                return compMapping;
        }

        return null;
    }
#endif
}
