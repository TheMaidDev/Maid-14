using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Maid.Administration.AhelpHistory;

[AdminCommand(AdminFlags.Adminhelp)]
public sealed class AhelpHistoryCommand : LocalizedCommands
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override string Command => "ahelphistory";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-ahelphistory-args-error"));
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-ahelphistory-wrong-target", ("user", args[0])));
            return;
        }

        _systems.GetEntitySystem<AhelpHistorySystem>()
            .OpenFor(player, located.UserId, located.Username);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = _players.Sessions
            .Select(c => c.Name)
            .OrderBy(c => c)
            .ToList();

        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-ahelphistory-hint"));
    }
}
