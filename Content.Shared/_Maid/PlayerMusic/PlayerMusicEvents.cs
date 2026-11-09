using Robust.Shared.Serialization;

namespace Content.Shared._Maid.PlayerMusic;

[Serializable, NetSerializable]
public enum PlayerMusicUiKey : byte
{
    Key
}

/// <summary>
/// Play the track with the given index from the inserted cassette.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicPlayMessage(int track) : BoundUserInterfaceMessage
{
    public int Track { get; } = track;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicRepeatMessage(bool repeat) : BoundUserInterfaceMessage
{
    public bool Repeat { get; } = repeat;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicStopMessage : BoundUserInterfaceMessage;

/// <summary>
/// Client -> server. Record a song on the cassette that is inside the boombox.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicUploadEvent(NetEntity boombox, string name, byte[] data) : EntityEventArgs
{
    public NetEntity Boombox { get; } = boombox;
    public string Name { get; } = name;
    public byte[] Data { get; } = data;
}

/// <summary>
/// Client -> server. "I'm near a playing boombox and don't have this song yet."
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicRequestFileEvent(NetEntity boombox, string songId) : EntityEventArgs
{
    public NetEntity Boombox { get; } = boombox;
    public string SongId { get; } = songId;
}

/// <summary>
/// Server -> one client. The audio file for a song.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicFileEvent(string songId, byte[] data) : EntityEventArgs
{
    public string SongId { get; } = songId;
    public byte[] Data { get; } = data;
}

[Serializable, NetSerializable]
public enum PlayerMusicVisuals : byte
{
    Playing
}

public enum PlayerMusicVisualLayers : byte
{
    Overlay
}
