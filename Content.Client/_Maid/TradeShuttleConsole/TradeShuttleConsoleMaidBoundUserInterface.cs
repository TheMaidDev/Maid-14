using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Content.Shared._Maid.TradeShuttleConsole;

namespace Content.Client._Maid.TradeShuttleConsole;

[UsedImplicitly] // We name add Maid cause of https://github.com/space-wizards/RobustToolbox/pull/7073
public sealed class TradeShuttleConsoleMaidBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private TradeShuttleConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<TradeShuttleConsoleWindow>();
        _window.OnActionButtonPressed += SendButtonPressed;
    }

    private void SendButtonPressed()
    {
        SendMessage(new TradeShuttleConsoleButtonPressedMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not TradeShuttleConsoleUIState cast)
            return;

        _window?.UpdateState(cast);
    }
}
