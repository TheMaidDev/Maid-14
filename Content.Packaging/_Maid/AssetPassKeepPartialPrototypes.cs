using Robust.Packaging.AssetProcessing;

namespace Content.Packaging._Maid;

public sealed class AssetPassKeepPartialPrototypes : AssetPass
{
    protected override AssetFileAcceptResult AcceptFile(AssetFile file)
    {
        if (!IsPartialPrototype(file.Path))
            return AssetFileAcceptResult.Pass;

        SendFile(file);
        return AssetFileAcceptResult.Consumed;
    }

    // Kept in sync with MaidPartialPrototypes.TryGetPartialFileIndex
    // TODO: refactor that shit
    private static bool IsPartialPrototype(string path)
    {
        if (!path.StartsWith("Prototypes/"))
            return false;

        var filename = path[(path.LastIndexOf('/') + 1)..];
        return filename.Split('.') is [.., "partial", "yml" or "yaml"] or [.., "partial", _, "yml" or "yaml"];
    }
}
