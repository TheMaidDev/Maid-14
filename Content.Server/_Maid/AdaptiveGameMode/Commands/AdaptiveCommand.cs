using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Server._Maid.AdaptiveGameMode;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Collector;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Static;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._Maid.AdaptiveGameMode.Commands;

[ToolshedCommand(Name = "adaptive"), AdminCommand(AdminFlags.Round)]
public sealed class AdaptiveCommand : ToolshedCommand
{
    private AdaptiveRuleSystem? _adaptiveRuleSystem;

    [CommandImplementation("gettargetbudget")]
    public AdaptiveScore GetTargetBudget()
    {
        _adaptiveRuleSystem ??= GetSys<AdaptiveRuleSystem>();
        var query = EntityManager.EntityQueryEnumerator<AdaptiveRuleComponent>();
        if (query.MoveNext(out var uid, out var component))
        {
            var gameTicker = GetSys<GameTicker>();
            return _adaptiveRuleSystem.GetTargetBudget(component, gameTicker.RoundDuration());
        }
        return new AdaptiveScore();
    }

    [CommandImplementation("calculatescore")]
    public GetAdaptiveScoreEvent CalculateScore()
    {
        _adaptiveRuleSystem ??= GetSys<AdaptiveRuleSystem>();
        return _adaptiveRuleSystem.CalculateChaosScore();
    }

    [CommandImplementation("getchaos")]
    public float GetChaos([PipedArgument] GetAdaptiveScoreEvent input)
    {
        return input.ChaosScore;
    }

    [CommandImplementation("getcombat")]
    public float GetCombat([PipedArgument] GetAdaptiveScoreEvent input)
    {
        return input.CombatScore;
    }

    [CommandImplementation("getaverage")]
    public float GetAverage([PipedArgument] GetAdaptiveScoreEvent input)
    {
        return input.Average;
    }

    [CommandImplementation("get")]
    public AdaptiveRuleComponent? Get()
    {
        var enumerator = EntityManager.EntityQueryEnumerator<AdaptiveRuleComponent>();
        while (enumerator.MoveNext(out var uid, out var component))
        {
            return component;
        }
        return null;
    }

}
