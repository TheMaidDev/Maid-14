using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Maid.Prototypes;

/// <summary>
/// Which prototype files are partial (merged into existing prototypes instead of defining new ones):
/// everything under <see cref="PartialDirectories"/>, and files named <c>*.partial.yml</c> or <c>*.partial.N.yml</c>
/// (N is the merge order index).
/// </summary>
public static class MaidPartialPrototypes
{
    /// <summary>
    /// Directories, relative to the prototypes root, whose prototypes are all partial.
    /// </summary>
    public static readonly ResPath[] PartialDirectories =
    [
        // Overrides of upstream prototypes, e.g. pointing them at old-build assets in /Audio/_Maid/Override.
        new("_Maid/Override"),
    ];

    /// <summary>
    /// Registers the partial directories under <paramref name="prototypesRoot"/> and every partial file in <paramref name="files"/>.
    /// </summary>
    public static void Mark(IPrototypeManager prototypeManager, ResPath prototypesRoot, IEnumerable<ResPath> files)
    {
        foreach (var directory in PartialDirectories)
        {
            prototypeManager.PartialDirectory(prototypesRoot / directory, 0);
        }

        foreach (var path in files)
        {
            if (TryGetPartialFileIndex(path, out var index))
                prototypeManager.PartialFile(path, index);
        }
    }

    /// <summary>
    /// Whether <paramref name="file"/> is partial, either by directory or by name.
    /// </summary>
    public static bool IsPartial(ResPath prototypesRoot, ResPath file)
    {
        return TryGetPartialFileIndex(file, out _)
               || PartialDirectories.Any(directory => file.TryRelativeTo(prototypesRoot / directory, out _));
    }

    /// <summary>
    /// Merge order index of a <c>*.partial[.N].yml</c> file.
    /// </summary>
    public static bool TryGetPartialFileIndex(ResPath path, out int index)
    {
        index = 0;
        var filename = path.Filename;

        if (!filename.Contains(".partial.")) // less allocations
            return false;

        switch (filename.Split('.'))
        {
            case [.., "partial", "yaml" or "yml"]:
                return true;

            case [.., "partial", var number, "yaml" or "yml"]:
                if (!int.TryParse(number, out var parsed) || parsed < 0)
                    throw new PrototypeLoadException($"Positive index after \"partial.\" expected, {number} found.");

                index = parsed;
                return true;

            default:
                return false;
        }
    }
}
