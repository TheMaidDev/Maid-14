using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._Maid.Extraction;

[ToolshedCommand(Name = "extraction"), AdminCommand(AdminFlags.Admin)]
public sealed class ExtractionCommand : ToolshedCommand
{
    private ExtractionSystem? _extractionField;
    private ExtractionSystem Extraction => _extractionField ??= GetSys<ExtractionSystem>();

    [CommandImplementation("reset")]
    public IEnumerable<string> Reset([PipedArgument] IEnumerable<EntityUid> input)
    {
        foreach (var uid in input)
        {
            yield return Extraction.ResetCommand(uid);
        }
    }

    [CommandImplementation("reset")]
    public string Reset(EntityUid target)
    {
        return Extraction.ResetCommand(target);
    }

    [CommandImplementation("unlock")]
    public IEnumerable<string> Unlock([PipedArgument] IEnumerable<EntityUid> input)
    {
        foreach (var uid in input)
        {
            yield return Extraction.UnlockCommand(uid);
        }
    }

    [CommandImplementation("unlock")]
    public string Unlock(EntityUid target)
    {
        return Extraction.UnlockCommand(target);
    }
}
