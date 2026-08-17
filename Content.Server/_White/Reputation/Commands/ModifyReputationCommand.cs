// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._White.Reputation.Commands;

[AdminCommand(AdminFlags.Host)]
public sealed class ModifyReputationCommand : LocalizedCommands
{
    public override string Command => "modifyreput";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var repManager = IoCManager.Resolve<IEntityManager>().System<ReputationManager>();

        if (args.Length < 2)
        {
            shell.WriteLine(Loc.GetString("cmd-modifyreput-not-enough-args") + "\n" + Help);
            return;
        }

        if (!playerManager.TryGetPlayerDataByUsername(args[0], out var playerData))
        {
            shell.WriteLine(Loc.GetString("cmd-modifyreput-player-not-found", ("ckey", args[0])));
            return;
        }

        if (!float.TryParse(args[1], out var value))
        {
            shell.WriteLine(Loc.GetString("cmd-modifyreput-invalid-value", ("value", args[1])));
            return;
        }

        var uid = playerData.UserId;
        var admin = playerData.UserName;

        repManager.ModifyPlayerReputation(uid, value, admin);

        shell.WriteLine(Loc.GetString("cmd-modifyreput-result", ("value", args[1]), ("ckey", args[0])));
    }
}
