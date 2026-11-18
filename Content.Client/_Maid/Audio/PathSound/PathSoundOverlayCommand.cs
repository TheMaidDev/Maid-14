using Robust.Shared.Console;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Toggles the sound path debug overlay.
/// </summary>
public sealed class PathSoundOverlayCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PathSoundSystem _system = default!;

    public override string Command => "showsoundpath";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _system.OverlayEnabled = !_system.OverlayEnabled;

        shell.WriteLine($"Sound path overlay: {_system.OverlayEnabled}");
    }
}
