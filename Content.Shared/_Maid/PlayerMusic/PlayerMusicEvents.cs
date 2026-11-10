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

public static class PlayerMusicLimits
{
    /// <summary>
    /// Songs are sent in pieces of this size, never as one big message.
    /// </summary>
    public const int ChunkBytes = 32 * 1024;
}

/// <summary>
/// Client -> server. Starts recording a song onto the cassette in the boombox; the data follows in chunks.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicUploadStartEvent(NetEntity boombox, string name, int totalBytes) : EntityEventArgs
{
    public NetEntity Boombox { get; } = boombox;
    public string Name { get; } = name;
    public int TotalBytes { get; } = totalBytes;
}

/// <summary>
/// Client -> server. One piece of the song, chunks must arrive in order.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicUploadChunkEvent(int index, byte[] data) : EntityEventArgs
{
    public int Index { get; } = index;
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
/// Client -> server. "I walked away, stop sending this song."
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicCancelFileEvent(string songId) : EntityEventArgs
{
    public string SongId { get; } = songId;
}

/// <summary>
/// Server -> one client. A song is about to be sent in chunks.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicFileStartEvent(string songId, int totalBytes) : EntityEventArgs
{
    public string SongId { get; } = songId;
    public int TotalBytes { get; } = totalBytes;
}

/// <summary>
/// Server -> one client. One piece of the song.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicFileChunkEvent(string songId, int index, byte[] data) : EntityEventArgs
{
    public string SongId { get; } = songId;
    public int Index { get; } = index;
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
