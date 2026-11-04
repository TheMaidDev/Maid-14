using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Debug: freezes the station sun light data. If the flicker stays, it comes from drawing, if it stops, from changing data.
/// </summary>
[AnyCommand]
public sealed class SunFreezeCommand : LocalizedCommands
{
    public override string Command => "sunfreeze";
    public override string Description => "Freezes or resumes the station sun light recomputation (flicker debugging).";
    public override string Help => "sunfreeze";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        StationSunShadowOverlay.Frozen = !StationSunShadowOverlay.Frozen;
        shell.WriteLine(StationSunShadowOverlay.Frozen ? "Sun light frozen." : "Sun light running.");
    }
}
