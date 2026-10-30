using System.Diagnostics;
using Content.Server._Maid.Antag;
using Content.Server.Administration;
using Content.Shared._Maid.Antag;
using Content.Shared.Administration;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Errors;
using Robust.Shared.Utility;

namespace Content.Server._Maid.Toolshed.Commands;

public record struct DeantagError(string Reason) : IConError
{
    public FormattedMessage DescribeInner()
    {
        return FormattedMessage.FromUnformatted(Reason);
    }

    public string? Expression { get; set; }
    public Vector2i? IssueSpan { get; set; }
    public StackTrace? Trace { get; set; }
}

[ToolshedCommand(Name = "deantag"), AdminCommand(AdminFlags.Admin)]
public sealed class DeantagToolshedCommand : ToolshedCommand
{
    private DeantagSystem? _deantagField;
    private DeantagSystem Deantag => _deantagField ??= GetSys<DeantagSystem>();

    /// <summary>
    ///     Removes every antag from the piped entities.
    /// </summary>
    [CommandImplementation]
    public IEnumerable<EntityUid> All(
        [CommandInvocationContext] IInvocationContext ctx,
        [PipedArgument] IEnumerable<EntityUid> input)
    {
        foreach (var ent in input)
        {
            if (!Deantag.TryGetMind(ent, out var mind))
            {
                ctx.ReportError(new DeantagError($"Entity {ent} has no mind."));
                continue;
            }

            Deantag.Deantag(mind);
            yield return ent;
        }
    }

    /// <summary>
    ///     Removes only the antag with the given deantag prototype id from the piped entities.
    /// </summary>
    [CommandImplementation]
    public IEnumerable<EntityUid> Single(
        [CommandInvocationContext] IInvocationContext ctx,
        [PipedArgument] IEnumerable<EntityUid> input,
        [CommandArgument] ProtoId<DeantagPrototype> id)
    {
        foreach (var ent in input)
        {
            if (!Deantag.TryGetMind(ent, out var mind))
            {
                ctx.ReportError(new DeantagError($"Entity {ent} has no mind."));
                continue;
            }

            Deantag.Deantag(mind, id);
            yield return ent;
        }
    }
}
