using System.Linq;
using System.Collections;
using System.Reflection;
using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;
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

    private IEnumerable<IAdaptiveScoreCondition> GetConditions(AdaptiveScoreCollectorComponent comp)
    {
        return comp.ConditionTables
            .SelectMany(table =>
                _protoManager.TryIndex(table, out var proto)
                    ? proto.Conditions
                    : []
            )
            .Concat(comp.Conditions);
    }

    private IEnumerable<EntityUid> GetEntities()
    {
        return _entityManager.GetEntities();
    }

    private IEnumerable<EntityUid> GetEntities(Type componentType)
    {
        foreach (var (uid, _) in _entityManager.GetAllComponents(componentType))
        {
            yield return uid;
        }
    }

    private void OnGetAdaptiveScore(ref GetAdaptiveScoreEvent ev)
    {
        var query = EntityQueryEnumerator<AdaptiveScoreCollectorComponent>();

        while (query.MoveNext(out var uid, out var collector))
        {
            var entities = collector.EnumerateComponent is not null
                           && _componentFactory.TryGetRegistration(collector.EnumerateComponent, out var reg)
                ? GetEntities(reg.Type)
                : GetEntities();

            var conditions = GetConditions(collector).ToArray();
            foreach (var ent in entities)
            {
                EntityUid? mob = null;
                Entity<MindComponent>? mind = null;

                if (TryComp<MindRoleComponent>(ent, out var mindRole))
                {
                    var mindId = mindRole.Mind.Owner;
                    if (TryComp<MindComponent>(mindId, out var mindComp))
                    {
                        mob = mindRole.Mind.Comp.OwnedEntity;
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

                if (conditions.All(condition => condition.ConditionMet(ent, mob, mind, _entityManager)))
                {
                    ev.Add(ent, collector.ChaosScore, collector.CombatScore);
                }
            }
        }
    }
#if DEBUG
    public IEnumerable<AdaptiveBalanceInfo> GetBalanceInfo()
    {
        static string FixName(string name)
        {
            if (name.StartsWith("AdaptiveScore"))
                name = name["AdaptiveScore".Length..];

            if (name.EndsWith("Condition"))
                name = name[..^"Condition".Length];

            return name;
        }

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
                                .Select(cond => cond.GetType().Name)
                                .Select(FixName)
                                .Concat(component.ConditionTables
                                    .Select(t => t.Id)
                                )
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
