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

        _window.OnPlayTrack += (track, trackId, offset) => SendMessage(new PlayerMusicPlayMessage(track, trackId, offset));
        _window.OnStop += () => SendMessage(new PlayerMusicStopMessage());
        _window.OnRepeat += repeat => SendMessage(new PlayerMusicRepeatMessage(repeat));
        _window.OnSeek += position => SendMessage(new PlayerMusicSeekMessage(position));
        _window.OnSetVolume += scale => SendMessage(new PlayerMusicVolumeMessage(scale));
        _window.OnDeleteTrack += (track, trackId) => SendMessage(new PlayerMusicDeleteTrackMessage(track, trackId));
        _window.OnUpload += (name, data) => EntMan.System<PlayerMusicSystem>().RequestUpload(Owner, name, data);

        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EntMan.System<PlayerMusicSystem>().CancelUpload();

        base.Dispose(disposing);
    }

    public void Reload()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out PlayerMusicBoomboxComponent? comp))
            return;

        _window.Update(comp);
    }

    public void SetStatus(string locKey, string text, float number)
    {
        _window?.SetStatus(locKey, text, number);
    }

    public void SetUploadProgress(int percent)
    {
        _window?.SetUploadProgress(percent);
    }
}
