using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared._Maid.CVars;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._Maid.Chat.Commands;

/// <summary>
///     Toggles the in-character slang autoreplacement at runtime.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SlangSanitizationCommand : LocalizedCommands
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override string Command => "enableslangsanitization";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[] { "true", "false" },
                LocalizationManager.GetString("cmd-enableslangsanitization-arg-enabled"));
        }

        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(LocalizationManager.GetString("shell-need-exactly-one-argument"));
            return;
        }

        if (!bool.TryParse(args[0], out var value))
        {
            shell.WriteError(LocalizationManager.GetString("shell-argument-must-be-boolean"));
            return;
        }

        _cfg.SetCVar(MaidCVars.ChatSlangFilter, value);

        var announce = LocalizationManager.GetString("chatsan-announce-slang-sanitization",
            ("admin", $"{shell.Player?.Name}"),
            ("value", $"{value}"));

        _chat.DispatchServerAnnouncement(announce, Color.Red);
    }
}
