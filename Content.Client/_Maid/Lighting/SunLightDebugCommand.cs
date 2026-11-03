using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Shared.Console;

namespace Content.Client._Maid.Lighting;

[AnyCommand]
public sealed class SunLightDebugCommand : LocalizedCommands
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override string Command => "sunlightdebug";
    public override string Description => "Toggles the sunlight debug overlay (ambient, roof and shadow values).";
    public override string Help => "sunlightdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_overlay.HasOverlay<SunLightDebugOverlay>())
        {
            _overlay.RemoveOverlay<SunLightDebugOverlay>();
            shell.WriteLine("Sunlight debug overlay off.");
            return;
        }

        _overlay.AddOverlay(new SunLightDebugOverlay());
        shell.WriteLine("Sunlight debug overlay on.");
    }
}
