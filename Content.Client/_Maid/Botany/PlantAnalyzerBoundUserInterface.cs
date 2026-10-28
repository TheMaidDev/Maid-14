using Content.Shared._Maid.Botany;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Maid.Botany;

[UsedImplicitly]
public sealed class PlantAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PlantAnalyzerWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PlantAnalyzerWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PlantAnalyzerState analyzer)
            _window?.Update(analyzer);
    }
}
