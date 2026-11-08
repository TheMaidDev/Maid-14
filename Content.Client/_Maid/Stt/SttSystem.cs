using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._Maid.CVars;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Maid.Stt;

/// <summary>
/// Receives recognized speech from the external STT companion program.
/// The companion drops one text file per phrase into <c>stt/inbox</c> inside the user data directory.
/// Depending on <see cref="MaidCVars.SttConfirm"/> the phrase is either placed into the chat input
/// for review or submitted immediately.
/// </summary>
public sealed class SttSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ResPath Root = new("/stt");
    private static readonly ResPath Inbox = Root / "inbox";
    private static readonly ResPath Control = Root / "control.txt";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private ISawmill _sawmill = default!;
    private TimeSpan _nextPoll;
    private bool _recording;

    public bool Recording => _recording;

    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("stt");
        EnsureDirs();
    }

    public override void Shutdown()
    {
        if (_recording)
            SetRecording(false);
    }

    private void EnsureDirs()
    {
        try
        {
            _res.UserData.CreateDir(Root);
            _res.UserData.CreateDir(Inbox);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to create STT directories: {e}");
        }
    }

    /// <summary>
    /// Asks the companion to start or stop recording (it watches <c>stt/control.txt</c>).
    /// </summary>
    public void SetRecording(bool record)
    {
        _recording = record;

        try
        {
            using var writer = _res.UserData.OpenWriteText(Control);
            writer.Write(record ? "start" : "stop");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to write STT control file: {e}");
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_timing.RealTime < _nextPoll)
            return;

        _nextPoll = _timing.RealTime + PollInterval;

        if (!_cfg.GetCVar(MaidCVars.SttEnabled))
            return;

        List<string> files;
        try
        {
            if (!_res.UserData.IsDir(Inbox))
                return;

            files = new List<string>(_res.UserData.DirectoryEntries(Inbox));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to read STT inbox: {e}");
            return;
        }

        // File names are timestamps from the companion, so ordinal sort keeps phrases in order.
        files.Sort(StringComparer.Ordinal);

        foreach (var name in files)
        {
            var path = Inbox / name;
            string text;
            try
            {
                // The companion writes "*.tmp" first and renames it when complete.
                if (name.EndsWith(".tmp", StringComparison.Ordinal) || _res.UserData.IsDir(path))
                    continue;

                if (!_res.UserData.TryReadAllText(path, out var read))
                    continue; // Possibly still being written, try next poll.

                text = read;
                _res.UserData.Delete(path);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to read STT message {name}: {e}");
                continue;
            }

            HandleText(text);
        }
    }

    private void HandleText(string raw)
    {
        var text = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var max = _cfg.GetCVar(MaidCVars.SttMaxLength);
        if (text.Length > max)
            text = text[..max];

        if (string.IsNullOrWhiteSpace(text))
            return;

        var box = _ui.ActiveScreen?.GetWidget<ChatBox>() ?? _ui.ActiveScreen?.GetWidget<ResizableChatBox>();
        if (box == null)
        {
            _sawmill.Warning("Got STT text but there is no chat box on the active screen.");
            return;
        }

        var input = box.ChatInput.Input;
        input.SetText(text);

        if (_cfg.GetCVar(MaidCVars.SttConfirm))
        {
            // Let the player review and edit; Enter sends it as usual.
            input.GrabKeyboardFocus();
            input.CursorPosition = input.Text.Length;
        }
        else
        {
            input.ForceSubmitText();
        }
    }
}
