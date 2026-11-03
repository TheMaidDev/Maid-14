using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Shared.Console;

namespace Content.Client._Maid.Lighting;

[AnyCommand]
public sealed class SunLightRaysCommand : LocalizedCommands
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override string Command => "sunlightrays";
    public override string Description => "Toggles sunlight direction arrows and highlights tiles open to the sun.";
    public override string Help => "sunlightrays";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_overlay.HasOverlay<SunLightRaysOverlay>())
        {
            _overlay.RemoveOverlay<SunLightRaysOverlay>();
            shell.WriteLine("Sunlight rays overlay off.");
            return;
        }

        _overlay.AddOverlay(new SunLightRaysOverlay());
        shell.WriteLine("Sunlight rays overlay on.");
    }
}
