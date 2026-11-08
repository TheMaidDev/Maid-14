using Content.Client._Maid.Stt;
using Content.Shared._Maid.CVars;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Client._Maid.Commands;

/// <summary>
/// Toggles the STT confirmation step: when on, recognized text is put into the chat input for review.
/// </summary>
[AnyCommand]
public sealed class SttConfirmCommand : LocalizedCommands
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override string Command => "stt_confirm";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var value = args.Length > 0 && bool.TryParse(args[0], out var parsed)
            ? parsed
            : !_cfg.GetCVar(MaidCVars.SttConfirm);

        _cfg.SetCVar(MaidCVars.SttConfirm, value);
        shell.WriteLine(Loc.GetString(value ? "stt-confirm-on" : "stt-confirm-off"));
    }
}

/// <summary>
/// Starts or stops recording in the external STT companion (bindable to a key).
/// </summary>
[AnyCommand]
public sealed class SttRecordCommand : LocalizedCommands
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public override string Command => "stt_record";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var stt = _systems.GetEntitySystem<SttSystem>();
        var value = args.Length > 0 && bool.TryParse(args[0], out var parsed)
            ? parsed
            : !stt.Recording;

        stt.SetRecording(value);
        shell.WriteLine(Loc.GetString(value ? "stt-record-on" : "stt-record-off"));
    }
}
