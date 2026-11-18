using Robust.Shared.Console;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Dumps the last applied sound path values for managed audio streams.
/// </summary>
public sealed class PathSoundDebugCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PathSoundSystem _system = default!;

    public override string Command => "soundpathdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_system.DumpDebug());
    }
}
