using Content.Client.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._Maid.Lighting;

[AnyCommand]
public sealed class SunDebugWindowCommand : LocalizedCommands
{
    [Dependency] private readonly IClientAdminManager _admin = default!;

    public override string Command => "sunlight";
    public override string Description => "Opens or closes the sunlight debug window (admins only).";
    public override string Help => "sunlight";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_admin.HasFlag(AdminFlags.Debug))
        {
            shell.WriteError("This window is for admins with the Debug flag.");
            return;
        }

        SunDebugWindow.Toggle();
    }
}
