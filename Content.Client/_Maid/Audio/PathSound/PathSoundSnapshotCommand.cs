using Robust.Shared.Console;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>
///     Writes a JSON snapshot of the sound path model state (fields, tile weights, streams, routes,
///     recent decisions) into the client user data dir for offline analysis.
/// </summary>
public sealed class PathSoundSnapshotCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PathSoundSystem _system = default!;

    public override string Command => "soundpathsnap";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine($"Sound path snapshot: {_system.WriteSnapshot(args.Length > 0 ? args[0] : null)}");
    }
}
