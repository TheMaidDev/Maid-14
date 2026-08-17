using System.Collections.Generic;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._Maid.AdaptiveGameMode;
using Content.Shared._Maid.CVars;
using Content.Shared.Eui;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Maid.AdaptiveGameMode;

public sealed class AdaptiveStatsEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;

    public AdaptiveStatsEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        if (_entManager.TrySystem<AdaptiveRuleBalancingSystem>(out var sys))
        {
            sys.RegisterEui(this);
        }
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        if (_entManager.TrySystem<AdaptiveRuleBalancingSystem>(out var sys))
        {
            sys.UnregisterEui(this);
        }
    }

    public override EuiStateBase GetNewState()
    {
        var roundData = new Dictionary<int, List<SharedAdaptiveCalculationRun>>();
        var currentRoundId = 0;
        var trackingEnabled = _cfg.GetCVar(MaidCVars.AdaptiveStatistics);

        if (_entManager.TrySystem<GameTicker>(out var ticker))
        {
            currentRoundId = ticker.RoundId;
        }

        if (_entManager.TrySystem<AdaptiveRuleBalancingSystem>(out var balancing))
        {
            foreach (var (roundId, runs) in balancing.GetRoundData())
            {
                var sharedRuns = new List<SharedAdaptiveCalculationRun>();
                foreach (var run in runs)
                {
                    var sharedRun = new SharedAdaptiveCalculationRun
                    {
                        Id = run.Id,
                        Time = run.Time,
                        TotalChaos = run.TotalChaos,
                        TotalCombat = run.TotalCombat,
                        TargetChaos = run.TargetChaos,
                        TargetCombat = run.TargetCombat
                    };

                    foreach (var record in run.Records)
                    {
                        sharedRun.Records.Add(new SharedAdaptiveScoreRecord
                        {
                            Entity = _entManager.GetNetEntity(record.Entity),
                            Name = record.Name,
                            Prototype = record.Prototype,
                            Chaos = record.Chaos,
                            Combat = record.Combat
                        });
                    }

                    sharedRuns.Add(sharedRun);
                }
                roundData[roundId] = sharedRuns;
            }
        }

        return new AdaptiveStatsEuiState(roundData, currentRoundId, trackingEnabled);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_adminManager.HasAdminFlag(Player, AdminFlags.Round))
            return;

        switch (msg)
        {
            case AdaptiveStatsToggleMessage toggleMsg:
                _cfg.SetCVar(MaidCVars.AdaptiveStatistics, toggleMsg.Enabled);
                StateDirty();
                break;
            case AdaptiveStatsCalculateMessage:
                if (_entManager.TrySystem<AdaptiveRuleSystem>(out var ruleSys))
                {
                    ruleSys.CalculateChaosScore();
                }
                break;
        }
    }
}
