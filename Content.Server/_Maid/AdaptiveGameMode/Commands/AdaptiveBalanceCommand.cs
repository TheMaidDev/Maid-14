using System.Collections.Generic;
using System.Linq;
using Content.Server._Maid.AdaptiveGameMode.MetaInfo;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._Maid.CVars;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Errors;

namespace Content.Server._Maid.AdaptiveGameMode.Commands;

[ToolshedCommand(Name = "adaptivebalancing"), AdminCommand(AdminFlags.Round)]
public sealed class AdaptiveBalanceCommand : ToolshedCommand
{
    [Dependency] private readonly EuiManager _euiManager = default!;

    [CommandImplementation("showstatsui")]
    public void ShowStatsUi(IInvocationContext ctx)
    {
        if (ctx.Session is null)
        {
            ctx.ReportError(new NotForServerConsoleError());
            return;
        }

        var ui = new AdaptiveStatsEui();
        _euiManager.OpenEui(ui, ctx.Session);
    }

    [CommandImplementation("calculatesexpectedscores")]
    public string ExpectedScoresStart([PipedArgument] List<AdaptiveRuleParam> rules)
    {
        var sysManager = IoCManager.Resolve<IEntitySystemManager>();
        var ruleSys = sysManager.GetEntitySystem<AdaptiveRuleSystem>();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("entity,expectedscore,combatscore");

        foreach (var rule in rules)
        {
            var score = ruleSys.CalculatePossibleScoreForPrototype(rule.Id);
            sb.AppendLine($"{rule.Id},{score.Chaos},{score.Combat}");
        }

        return sb.ToString();
    }

    #if DEBUG
    [CommandImplementation("calculatebalancetable")]
    public string CalculateBalanceTable()
    {
        var cfg = IoCManager.Resolve<IConfigurationManager>();
        if (!cfg.GetCVar(CCVars.ConfigPresetDevelopment))
            return "This command can only be run in a development environment.";

        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var entitySystemManager = IoCManager.Resolve<IEntitySystemManager>();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Entity,Condition/Component,Chaos From,Chaos To,Chaos Duration,Combat From,Combat To,Combat Duration");

        var providers = new List<IAdaptiveBalanceInfoProvider>();
        foreach (var type in entitySystemManager.GetEntitySystemTypes())
        {
            if (typeof(IAdaptiveBalanceInfoProvider).IsAssignableFrom(type) &&
                entitySystemManager.TryGetEntitySystem(type, out var system) &&
                system is IAdaptiveBalanceInfoProvider provider)
            {
                sb.AppendLine(string.Join(
                    "\n",
                    provider
                        .GetBalanceInfo()
                        .Select(info => info.ToString())
                ));
            }
        }

        return sb.ToString();
    }
    #endif

    [AdminCommand(AdminFlags.Round)]
    public sealed class ToggleAdaptiveStatsCommand : IConsoleCommand
    {
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        public string Command => "adaptivetogglestats";
        public string Description => "Enables or disables adaptive rule balancing statistics tracking.";
        public string Help => $"{Command} | {Command} <true/false>";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var value = !_cfg.GetCVar(MaidCVars.AdaptiveStatistics);

            if (args.Length > 0)
            {
                if (!bool.TryParse(args[0], out value))
                {
                    shell.WriteError("Invalid boolean argument. Use 'true' or 'false'.");
                    return;
                }
            }

            _cfg.SetCVar(MaidCVars.AdaptiveStatistics, value);
            shell.WriteLine($"Adaptive statistics tracking has been {(value ? "enabled" : "disabled")}.");
        }
    }
}
