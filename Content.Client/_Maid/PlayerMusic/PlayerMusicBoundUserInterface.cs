using Content.Shared._Maid.PlayerMusic;
using Robust.Client.UserInterface;

namespace Content.Client._Maid.PlayerMusic;

public sealed class PlayerMusicBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PlayerMusicWindow? _window;

    public PlayerMusicBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        // The engine can queue the open twice when the entity has not been synced to the client yet;
        // opening twice would create two overlapping windows
        if (_window != null)
        {
            Reload();
            return;
        }

        _window = this.CreateWindow<PlayerMusicWindow>();

        _window.OnPlayTrack += (track, trackId) => SendMessage(new PlayerMusicPlayMessage(track, trackId));
        _window.OnStop += () => SendMessage(new PlayerMusicStopMessage());
        _window.OnRepeat += repeat => SendMessage(new PlayerMusicRepeatMessage(repeat));
        _window.OnUpload += (name, data) => EntMan.System<PlayerMusicSystem>().RequestUpload(Owner, name, data);

        Reload();
    }

    public void Reload()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out PlayerMusicBoomboxComponent? comp))
            return;

        _window.Update(comp);
    }
}
