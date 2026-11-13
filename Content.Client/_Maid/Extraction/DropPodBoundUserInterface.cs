using Content.Shared._Maid.Extraction;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Maid.Extraction;

[UsedImplicitly]
public sealed class DropPodBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private DropPodMenu? _menu;

    public DropPodBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<DropPodMenu>();
        _menu.OnKeypadButtonPressed += i => SendMessage(new DropPodKeypadMessage(i));
        _menu.OnClearButtonPressed += () => SendMessage(new DropPodKeypadClearMessage());
        _menu.OnEnterButtonPressed += () => SendMessage(new DropPodKeypadEnterMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DropPodUiState podState)
            _menu?.UpdateState(podState);
    }
}
