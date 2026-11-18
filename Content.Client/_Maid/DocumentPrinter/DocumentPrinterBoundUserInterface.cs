using Content.Shared._Maid.DocumentPrinter;
using Content.Shared.Cargo.Events;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Maid.DocumentPrinter;

[UsedImplicitly]
public sealed class DocumentPrinterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DocumentPrinterWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DocumentPrinterWindow>();
        _window.OnPrint += document => SendMessage(new DocumentPrinterPrintMessage(document));
        _window.OnOrder += (requester, reason, product, amount) =>
            SendMessage(new CargoConsoleAddOrderMessage(requester, reason, product, amount));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DocumentPrinterBuiState cast)
            _window?.UpdateState(cast);
    }
}
