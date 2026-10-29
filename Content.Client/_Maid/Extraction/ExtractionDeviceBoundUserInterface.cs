using Content.Shared._Maid.Extraction;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Maid.Extraction;

[UsedImplicitly]
public sealed class ExtractionDeviceBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ExtractionDeviceMenu? _menu;

    public ExtractionDeviceBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ExtractionDeviceMenu>();
        _menu.RefreshButton.OnPressed += _ => SendMessage(new ExtractionDeviceRefreshMessage());
        _menu.OnZoneRequested += () => SendMessage(new ExtractionDeviceRequestZoneMessage());
        _menu.PodButton.OnPressed += _ => SendMessage(new ExtractionDeviceCallPodMessage());

        // the server's update on open can arrive before the UI exists here, so ask again
        SendMessage(new ExtractionDeviceRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is ExtractionDeviceUiState deviceState)
            _menu?.UpdateState(deviceState);
    }
}
