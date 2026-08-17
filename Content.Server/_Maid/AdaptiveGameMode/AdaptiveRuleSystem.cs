using Content.Server.GameTicking.Rules;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Robust.Shared.Prototypes;
using System.Linq;
using Content.Server._Maid.AdaptiveGameMode;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Static;
using Content.Server.Administration.Logs;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Antag.Components;
using Robust.Server.Player;
using Content.Server.Antag;
using Robust.Shared.Random;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Content.Server.Preferences.Managers;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Random.Helpers;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Humanoid.Prototypes;
using Content.Server.StationEvents.Components;
using Content.Shared.Administration.Logs;


public sealed class AdaptiveRuleSystem : GameRuleSystem<AdaptiveRuleComponent>
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly AdaptiveRuleBalancingSystem _balancing = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    // I hate admin logger
    private new void Log(ref LogStringHandler handler, LogImpact impact = LogImpact.High)
    {
        _adminLog.Add(
            LogType.EventRan,
            impact,
            $"Adaptive: {handler.ToStringAndClear()}"
        );
    }

    protected override void Started(EntityUid uid, AdaptiveRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        Log($"AdaptiveRule is enabled");

        component.TimeUntilNextAttempt = component.MidroundSpawnTimer.GetValue(_random);

        SpawnRoundstartRules(uid, component);
    }

    private void SpawnRoundstartRules(EntityUid uid, AdaptiveRuleComponent component)
    {
        var readyPlayersCount = _playerManager.Sessions
            .Count(session => GameTicker.PlayerGameStatuses.TryGetValue(session.UserId, out var status)
                              && (status == PlayerGameStatus.ReadyToPlay || status == PlayerGameStatus.JoinedGame)
            );

        var candidateRules = component.RoundstartRules
            .Where(ruleParam =>
                ruleParam.Conditions.All(c => c.Condition(ruleParam, component, EntityManager))
            )
            .ToList();

        var roundstartTargetBudget = GetTargetBudget(component, TimeSpan.Zero) + readyPlayersCount * component.RoundstartScorePerPlayer;
        var accumulatedScore = new AdaptiveScore();

        Log($"Spawning roundstart rules with budget {roundstartTargetBudget} (of {readyPlayersCount} players)...");

        for (var i = 0; i < 5; i++) // for now - hardcoded limit. TODO
        {
            // TODO: Also temporary. Probably should do probability for spawning next rule based on distance to target score
            if (accumulatedScore.Average >= roundstartTargetBudget.Average)
            {
                Log($"Budget exceeded...");
                break;
            }

            if (candidateRules.Count == 0)
            {
                break;
            }

            var remainingBudget = roundstartTargetBudget - accumulatedScore;
            var chosenRule = ChooseRandomRule(component, candidateRules, remainingBudget, readyPlayersCount);

            if (chosenRule == null)
                break;

            if (SpawnRule(uid, component, chosenRule.Id) != null)
            {
                accumulatedScore += CalculatePossibleScoreForPrototype(chosenRule.Id, readyPlayersCount);
            }

            candidateRules.Remove(chosenRule); // can't spawn same rule multiple times
            // TODO: just do it with conditions and recalculate them. This will allow things like "rule allowed only if other is not runned", etc
        }

        Log($"Done spawning roundstart rules");
    }

    protected override void ActiveTick(EntityUid uid, AdaptiveRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.TimeUntilNextAttempt -= frameTime;
        if (component.TimeUntilNextAttempt > 0)
            return;

        // Reset timer
        component.TimeUntilNextAttempt = component.MidroundSpawnTimer.GetValue(_random);

        // Try spawning a rule
        TrySpawnRandomRule(uid, component);
    }

    private void TrySpawnRandomRule(EntityUid uid, AdaptiveRuleComponent component)
    {
        if (component.MidroundRules.Count == 0)
            return;

        // TODO: Put there chance based on how close we to target chaos score
        // For now i just kept it to make rule check frequent (i love statistics, i wasted like hour making chart system lol)
        if (_random.Prob(component.MidroundSpawnSkipProb))
        {
            Log($"Skipping rule spawning (UNLUCKY)");
            return;
        }

        // Evaluate conditions for candidate rules
        var candidateRules = new List<AdaptiveRuleParam>();
        foreach (var ruleParam in component.MidroundRules)
        {
            if (ruleParam.Conditions.All(c => c.Condition(ruleParam, component, EntityManager)))
            {
                candidateRules.Add(ruleParam);
            }
        }

        if (candidateRules.Count == 0)
            return;

        var currentScore = CalculateChaosScore();
        var targetBudget = GetTargetBudget(component, _gameTicker.RoundDuration());
        var scoreBudget = targetBudget - new AdaptiveScore { Chaos = currentScore.ChaosScore, Combat = currentScore.CombatScore };

        var chosenRule = ChooseRandomRule(component, candidateRules, scoreBudget);
        if (chosenRule == null)
            return;

        SpawnRule(uid, component, chosenRule.Id);
    }

    public AdaptiveRuleParam? ChooseRandomRule(
        AdaptiveRuleComponent component,
        List<AdaptiveRuleParam> rules,
        AdaptiveScore scoreBudget,
        int? playerCount = null)
    {
        // RobustRandom weight uses dictionaries, and i don't want to copy shit
        // So there manual implementation

        if (rules.Count == 0)
            return null;

        var weightedRules = GetRulesWeighted(component, rules, scoreBudget, playerCount);
        var sum = 0f;
        foreach (var (_, weight) in weightedRules)
        {
            if (weight > 0)
                sum += weight;
        }

        if (sum <= 0)
            return null;

        var r = _random.NextFloat() * sum;
        foreach (var (rule, weight) in weightedRules)
        {
            if (weight <= 0)
                continue;

            r -= weight;
            if (r <= 0)
                return rule;
        }

        return weightedRules.Last().Rule;
    }

    public List<(AdaptiveRuleParam Rule, float Weight)> GetRulesWeighted(
        AdaptiveRuleComponent component,
        List<AdaptiveRuleParam> rules,
        AdaptiveScore scoreBudget,
        int? playerCount = null)
    {
        var result = new List<(AdaptiveRuleParam Rule, float Weight)>();
        var decay = component.ScoreDifferenceMultiplierDecay;
        var m = component.ScoreDifferenceMultiplierMin;

        foreach (var rule in rules)
        {

            var expectedBudget = CalculatePossibleScoreForPrototype(rule.Id, playerCount);

            var multiplierChaos = GetMultiplier(scoreBudget.Chaos, expectedBudget.Chaos);
            var multiplierCombat = GetMultiplier(scoreBudget.Combat, expectedBudget.Combat);

            // Honestly, i don't sure about that
            // But this will make sure that we will NOT get some combat event (cause of large chaos)
            // when we have invasion or some shit idk
            // We also can't just multiply then cause it will decrease min from defined to min*min
            // I'm thinking about slope that will take average on small difference
            // but went to min on huge differences
            // but for now this should work fine
            var multiplier = MathF.Min(multiplierChaos, multiplierCombat);
            var weight = rule.BaseWeight * multiplier;
            result.Add((rule, weight));
        }

        return result;

        // Basic normal distribution (probably didn't think about it enough, just took what first came to mine head, should be fine tho)
        // https://www.desmos.com/calculator/uqlzvsqnsn?lang=ru
        float GetMultiplier(float budget, float expected)
        {
            var x = budget - expected;
            var exponent = -(x * x) / decay;
            return MathF.Exp(exponent) * (1f - m) + m;
        }
    }

    public AdaptiveScore CalculatePossibleScoreForPrototype(string ruleId, int? playerCount = null)
    {
        var totalScore = GetPrototypeStaticScore(ruleId);

        if (!_protoManager.TryIndex<EntityPrototype>(ruleId, out var proto))
            return totalScore;

        // We can't check everything (spawn tables is pain), so please, when it fails define manually
        if (proto.TryGetComponent(out RandomEntityStorageSpawnRuleComponent? storageSpawnRule, _compFactory))
        {
            totalScore += GetPrototypeStaticScore(storageSpawnRule.Prototype);
        }

        // Check for direct AdaptiveScoreStaticGameruleEntity
        if (proto.TryGetComponent(out AdaptiveScoreStaticGameruleEntityComponent? gameruleEntity, _compFactory))
        {
            totalScore += GetPrototypeStaticScore(gameruleEntity.Prototype) * gameruleEntity.Count;
        }

        // Check for direct AntagLoadProfileRule species spawns on the rule itself
        if (proto.TryGetComponent(out AntagLoadProfileRuleComponent? loadProfile, _compFactory))
        {
            var speciesId = loadProfile.SpeciesHardOverride;
            if (speciesId == null && loadProfile.AlwaysUseSpeciesOverride)
            {
                speciesId = loadProfile.SpeciesOverride;
            }

            if (speciesId != null && _protoManager.TryIndex<SpeciesPrototype>(speciesId.Value, out var species))
            {
                totalScore += GetPrototypeStaticScore(species.Prototype);
            }
        }

        // Check for direct RandomSpawnRule
        if (proto.TryGetComponent(out RandomSpawnRuleComponent? randomSpawnRule, _compFactory))
        {
            totalScore += GetPrototypeStaticScore(randomSpawnRule.Prototype);
        }


        // Check for direct AntagSpawner spawns on the rule itself
        if (proto.TryGetComponent(out AntagSpawnerComponent? ruleSpawner, _compFactory))
        {
            totalScore += GetPrototypeStaticScore(ruleSpawner.Prototype);
        }
        if (proto.TryGetComponent(out AntagSelectionComponent? antagComp, _compFactory))
        {
            var poolSize = playerCount ?? _antagSelection.GetTotalPlayerCount(_playerManager.Sessions);

            foreach (var def in antagComp.Definitions)
            {
                var countOffset = 0;
                foreach (var otherDef in antagComp.Definitions)
                {
                    countOffset += Math.Clamp((poolSize - countOffset) / otherDef.PlayerRatio, otherDef.Min, otherDef.Max) * otherDef.PlayerRatio;
                }
                countOffset -= Math.Clamp(poolSize / def.PlayerRatio, def.Min, def.Max) * def.PlayerRatio;
                var antagCount = Math.Clamp((poolSize - countOffset) / def.PlayerRatio, def.Min, def.Max);

                if (antagCount <= 0)
                    continue;

                // MindRoles
                if (def.MindRoles != null)
                {
                    foreach (var role in def.MindRoles)
                    {
                        totalScore += GetPrototypeStaticScore(role) * antagCount;
                    }
                }

                // Spawners
                if (def.SpawnerPrototype != null)
                {
                    totalScore += GetPrototypeStaticScore(def.SpawnerPrototype) * antagCount;
                }

                // Added components
                var staticCompName = _compFactory.GetComponentName<AdaptiveScoreStaticComponent>();
                if (def.Components.TryGetValue(staticCompName, out var staticCompEntry))
                {
                    var staticComp = (AdaptiveScoreStaticComponent) staticCompEntry.Component;
                    totalScore += (AdaptiveScore) staticComp * antagCount;
                }
            }
        }

        return totalScore;
    }

    public EntityUid? SpawnRule(EntityUid uid, AdaptiveRuleComponent component, string ruleId)
    {
        if (!GameTicker.StartGameRule(ruleId, out var ruleEnt))
            return null;

        component.SpawnedRules.Add(new AdaptiveSpawnedRule
        {
            RuleId = ruleId,
            Entity = ruleEnt,
            SpawnTime = Timing.CurTime
        });

        Log($"Rule spawned: {ruleId}");

        return ruleEnt;

    }

    public GetAdaptiveScoreEvent CalculateChaosScore()
    {
        var ev = new GetAdaptiveScoreEvent();

        if (_balancing.TrackingEnabled)
        {
            ev.Records = [];
        }

        RaiseLocalEvent(ref ev);

        if (!_balancing.TrackingEnabled || ev.Records == null)
            return ev;

        var targetBudget = new AdaptiveScore();
        var query = EntityQueryEnumerator<AdaptiveRuleComponent>();
        if (query.MoveNext(out var uid, out var comp))
        {
            targetBudget = GetTargetBudget(comp, _gameTicker.RoundDuration());
        }

        // Kinda gross but it is what it is
        _balancing.SaveCalculationRun(ev.ChaosScore, ev.CombatScore, targetBudget.Chaos, targetBudget.Combat, ev.Records);

        return ev;
    }

    /// <summary>
    /// Calculates the potential score for a given gamerule entity prototype.
    /// This resolves the prototype's own static score, plus the scores of any mind roles it spawns.
    /// </summary>
    public AdaptiveScore CalculatePossibleScoreForDefinition(Entity<AntagSelectionComponent> antagSelection, int? playerCount = null)
    {
        var totalScore = MetaData(antagSelection).EntityPrototype?.ID is {} id
            ? GetPrototypeStaticScore(id)
            : new();

        var antagComp = antagSelection.Comp;

        // Check for direct AdaptiveScoreStaticGameruleEntity
        if (TryComp<AdaptiveScoreStaticGameruleEntityComponent>(antagSelection, out var gameruleEntity))
        {
            totalScore += GetPrototypeStaticScore(gameruleEntity.Prototype) * gameruleEntity.Count;
        }

        // Check for direct AntagLoadProfileRule species spawns on the rule itself
        if (TryComp<AntagLoadProfileRuleComponent>(antagSelection, out var loadProfile))
        {
            var speciesId = loadProfile.SpeciesHardOverride;
            if (speciesId == null && loadProfile.AlwaysUseSpeciesOverride)
            {
                speciesId = loadProfile.SpeciesOverride;
            }

            if (speciesId != null && _protoManager.TryIndex<SpeciesPrototype>(speciesId.Value, out var species))
            {
                totalScore += GetPrototypeStaticScore(species.Prototype);
            }
        }

        // Check for direct RandomSpawnRule
        if (TryComp<RandomSpawnRuleComponent>(antagSelection, out var randomSpawnRule))
        {
            totalScore += GetPrototypeStaticScore(randomSpawnRule.Prototype);
        }


        // Check for direct AntagSpawner spawns on the rule itself
        if (TryComp<AntagSpawnerComponent>(antagSelection, out var ruleSpawner))
        {
            totalScore += GetPrototypeStaticScore(ruleSpawner.Prototype);
        }
        foreach (var def in antagComp.Definitions)
        {
            var antagCount = _antagSelection.GetTargetAntagCount(antagSelection, playerCount, def);
            if (antagCount <= 0)
                continue;

            // MindRoles
            if (def.MindRoles != null)
            {
                foreach (var role in def.MindRoles)
                {
                    totalScore += GetPrototypeStaticScore(role) * antagCount;
                }
            }

            // Spawners
            if (def.SpawnerPrototype != null)
            {
                totalScore += GetPrototypeStaticScore(def.SpawnerPrototype) * antagCount;
            }

            // Added components (i will kill you if you will add static score components like that)
            var staticCompName = _compFactory.GetComponentName<AdaptiveScoreStaticComponent>();
            if (def.Components.TryGetValue(staticCompName, out var staticCompEntry))
            {
                var staticComp = (AdaptiveScoreStaticComponent) staticCompEntry.Component;
                totalScore += (AdaptiveScore) staticComp * antagCount;
            }
        }

        return totalScore;
    }

    private AdaptiveScore GetPrototypeStaticScore(string protoId, int depth = 0)
    {
        if (depth > 15)
        {
            _adminLog.Add(LogType.EventRan, LogImpact.Extreme, $"Adaptive: SOMETHING IS BROKEN!! We at {protoId}");
            return new(); // Something definitely broke
        }

        var score = new AdaptiveScore();

        if (!_protoManager.TryIndex<EntityPrototype>(protoId, out var proto))
            return new();

        // Has AdaptiveScoreStaticComponent
        if (proto.TryGetComponent(out AdaptiveScoreStaticComponent? staticScore, _compFactory))
        {
            score += staticScore;
        }

        // Is spawner
        if (proto.TryGetComponent(out GhostRoleComponent? ghostComp, _compFactory))
        {
            foreach (var role in ghostComp.MindRoles)
            {
                score += GetPrototypeStaticScore(role, depth + 1);
            }
        }

        // Is mob spawner
        if (proto.TryGetComponent(out GhostRoleMobSpawnerComponent? spawnerComp, _compFactory))
        {
            if (spawnerComp.Prototype != null)
            {
                score += GetPrototypeStaticScore(spawnerComp.Prototype, depth + 1);
            }

            foreach (var selectable in spawnerComp.SelectablePrototypes)
            {
                score += GetPrototypeStaticScore(selectable, depth + 1);
            }
        }

        // Is antag spawner
        if (proto.TryGetComponent(out AntagSpawnerComponent? antagSpawner, _compFactory))
        {
            score += GetPrototypeStaticScore(antagSpawner.Prototype, depth + 1);
        }

        // Is conditional or random spawner
        if (proto.TryGetComponent(out Content.Server.Spawners.Components.ConditionalSpawnerComponent? condSpawner, _compFactory))
        {
            foreach (var spawnerProto in condSpawner.Prototypes)
            {
                score += GetPrototypeStaticScore(spawnerProto, depth + 1);
            }
        }

        return score;
    }



    public AdaptiveScore GetTargetBudget(AdaptiveRuleComponent component, TimeSpan duration)
    {
        var seconds = (float) duration.TotalSeconds;
        if (component.RoundstartTargetBudgetSlope.Count == 0)
        {
            return component.RoundstartTargetBudgetBase;
        }

        var points = component.RoundstartTargetBudgetSlope.OrderBy(p => p.Time).ToList();

        if (seconds <= points[0].Time)
        {
            if (points[0].Time <= 0f)
                return points[0].Value;

            var ratio = seconds / points[0].Time;
            return component.RoundstartTargetBudgetBase + (points[0].Value - component.RoundstartTargetBudgetBase) * ratio;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            if (seconds >= p1.Time && seconds <= p2.Time)
            {
                var diff = p2.Time - p1.Time;
                if (diff <= 0f)
                    return p2.Value;

                var ratio = (seconds - p1.Time) / diff;
                return p1.Value + (p2.Value - p1.Value) * ratio;
            }
        }

        return points[^1].Value;
    }
}
