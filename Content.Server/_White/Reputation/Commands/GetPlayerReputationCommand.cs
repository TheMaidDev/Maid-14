// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._White.Reputation.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class GetPlayerReputationCommand : LocalizedCommands
{
    public override string Command => "getreput";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var repManager = IoCManager.Resolve<IEntityManager>().System<ReputationManager>();

        if (args.Length < 1)
        {
            shell.WriteLine(Loc.GetString("cmd-getreput-not-enough-args") + "\n" + Help);
            return;
        }

        if (!playerManager.TryGetPlayerDataByUsername(args[0], out var playerData))
        {
            shell.WriteLine(Loc.GetString("cmd-getreput-player-not-found", ("ckey", args[0])));
            return;
        }

        var uid = playerData.UserId;

        var value = await repManager.GetPlayerReputation(uid);

        if (value == null)
        {
            shell.WriteLine(Loc.GetString("cmd-getreput-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-getreput-result", ("ckey", args[0]), ("value", value)));
    }
}
