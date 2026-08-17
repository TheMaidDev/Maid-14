// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._White.Reputation.Commands;

[AnyCommand]
public sealed class ShowReputationCommand : LocalizedCommands
{
    public override string Command => "showreput";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null)
            return;

        var repManager = IoCManager.Resolve<IEntityManager>().System<ReputationManager>();

        var value = await repManager.GetPlayerReputation(shell.Player.UserId);
        if (value == null)
        {
            shell.WriteLine(Loc.GetString("cmd-showreput-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-showreput-result", ("value", value)));
    }
}
