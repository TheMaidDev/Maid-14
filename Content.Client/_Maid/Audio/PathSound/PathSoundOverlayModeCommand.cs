using Robust.Shared.Console;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Sets the sound path overlay display mode.
/// </summary>
public sealed class PathSoundOverlayModeCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PathSoundSystem _system = default!;

    public override string Command => "soundpathmode";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 ||
            !Enum.TryParse<PathSoundOverlayMode>(args[0], true, out var mode))
        {
            shell.WriteLine("usage: soundpathmode <off|field|numbers|all>");
            return;
        }

        _system.OverlayMode = mode;
        shell.WriteLine($"Sound path overlay mode: {_system.OverlayMode}");
    }
}
