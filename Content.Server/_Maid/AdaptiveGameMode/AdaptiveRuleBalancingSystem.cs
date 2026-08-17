using System;
using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Content.Shared._Maid.CVars;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Maid.AdaptiveGameMode;

public sealed class AdaptiveRuleBalancingSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    public bool TrackingEnabled => _cfg.GetCVar(MaidCVars.AdaptiveStatistics);

    // Dictionary of roundId -> list of calculation runs
    private readonly Dictionary<int, List<AdaptiveCalculationRun>> _roundData = new();
    private readonly HashSet<AdaptiveStatsEui> _openEuis = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        PruneRoundData();
    }

    private void PruneRoundData()
    {
        if (_roundData.Count <= 20)
            return;

        var sortedRounds = new List<int>(_roundData.Keys);
        sortedRounds.Sort();

        var roundsToRemoveCount = sortedRounds.Count - 20;
        for (var i = 0; i < roundsToRemoveCount; i++)
        {
            _roundData.Remove(sortedRounds[i]);
        }
    }

    public void RegisterEui(AdaptiveStatsEui eui)
    {
        _openEuis.Add(eui);
    }

    public void UnregisterEui(AdaptiveStatsEui eui)
    {
        _openEuis.Remove(eui);
    }

    public void UpdateEuis()
    {
        foreach (var eui in _openEuis)
        {
            eui.StateDirty();
        }
    }

    public void SaveCalculationRun(float totalChaos, float totalCombat, float targetChaos, float targetCombat, List<AdaptiveScoreRecord> records)
    {
        if (!TrackingEnabled)
            return;

        var roundId = _gameTicker.RoundId;
        if (!_roundData.TryGetValue(roundId, out var list))
        {
            list = new List<AdaptiveCalculationRun>();
            _roundData[roundId] = list;
            PruneRoundData();
        }

        var run = new AdaptiveCalculationRun
        {
            Id = list.Count + 1,
            Time = _gameTicker.RoundDuration(),
            TotalChaos = totalChaos,
            TotalCombat = totalCombat,
            TargetChaos = targetChaos,
            TargetCombat = targetCombat,
        };

        foreach (var record in records)
        {
            var name = "Unknown";
            string? prototype = null;

            if (record.Entity.IsValid())
            {
                name = Name(record.Entity);
                if (TryComp(record.Entity, out MetaDataComponent? meta))
                    prototype = meta.EntityPrototype?.ID;
            }

            run.Records.Add(new ServerAdaptiveScoreRecord(record.Entity, name, prototype, record.Chaos, record.Combat));
        }

        list.Add(run);
        UpdateEuis();
    }

    public IReadOnlyDictionary<int, List<AdaptiveCalculationRun>> GetRoundData()
    {
        return _roundData;
    }
}

public sealed class AdaptiveCalculationRun
{
    public int Id { get; set; }
    public TimeSpan Time { get; set; }
    public float TotalChaos { get; set; }
    public float TotalCombat { get; set; }
    public List<ServerAdaptiveScoreRecord> Records { get; } = new();
    public float TargetChaos { get; set; }
    public float TargetCombat { get; set; }
}

public record struct ServerAdaptiveScoreRecord(EntityUid Entity, string Name, string? Prototype, float Chaos, float Combat);
