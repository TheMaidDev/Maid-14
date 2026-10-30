using System.Linq;
using Content.Server.Administration;
using Content.Shared._Maid.Antag;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.Antag;

[AdminCommand(AdminFlags.Admin)]
public sealed class DeantagCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly DeantagSystem _deantag = default!;

    public override string Command => "deantag";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        EntityUid target;
        if (_players.TryGetSessionByUsername(args[0], out var session))
        {
            if (session.AttachedEntity is not { } attached)
            {
                shell.WriteError(Loc.GetString("cmd-deantag-no-entity"));
                return;
            }

            target = attached;
        }
        else if (NetEntity.TryParse(args[0], out var netEnt) && EntityManager.TryGetEntity(netEnt, out var ent))
        {
            target = ent.Value;
        }
        else
        {
            shell.WriteError(Loc.GetString("cmd-deantag-target-not-found", ("target", args[0])));
            return;
        }

        string? antagId = null;
        if (args.Length == 2 && !args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!_proto.HasIndex<DeantagPrototype>(args[1]))
            {
                shell.WriteError(Loc.GetString("cmd-deantag-unknown-id", ("id", args[1])));
                return;
            }

            antagId = args[1];
        }

        if (!_deantag.TryGetMind(target, out var mind))
        {
            shell.WriteError(Loc.GetString("cmd-deantag-no-mind"));
            return;
        }

        var removed = _deantag.Deantag(mind, antagId);
        shell.WriteLine(Loc.GetString("cmd-deantag-done", ("antags", string.Join(", ", removed))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("cmd-deantag-arg-target")),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<DeantagPrototype>().Append(new CompletionOption("all")),
                Loc.GetString("cmd-deantag-arg-id")),
            _ => CompletionResult.Empty,
        };
    }
}
