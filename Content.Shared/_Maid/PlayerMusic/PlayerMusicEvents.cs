using Robust.Shared.Serialization;

namespace Content.Shared._Maid.PlayerMusic;

[Serializable, NetSerializable]
public enum PlayerMusicUiKey : byte
{
    Key
}

/// <summary>
/// Play the track with the given index from the inserted cassette, starting at
/// <paramref name="offsetSeconds"/>. The expected track id is included so a cassette
/// swapped since the window was synced cannot start a wrong track.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerMusicPlayMessage(int track, string trackId, float offsetSeconds) : BoundUserInterfaceMessage
{
    public int Track { get; } = track;
    public string TrackId { get; } = trackId;
    public float OffsetSeconds { get; } = offsetSeconds;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicSeekMessage(float position) : BoundUserInterfaceMessage
{
    public float Position { get; } = position;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicVolumeMessage(float scale) : BoundUserInterfaceMessage
{
    public float Scale { get; } = scale;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicDeleteTrackMessage(int track, string trackId) : BoundUserInterfaceMessage
{
    public int Track { get; } = track;
    public string TrackId { get; } = trackId;
}

[Serializable, NetSerializable]
public sealed class PlayerMusicRecordStatusEvent(
    NetEntity boombox,
    string locKey,
    string text = "",
    float number = 0f) : EntityEventArgs
{
    public NetEntity Boombox { get; } = boombox;
    public string LocKey { get; } = locKey;
    public string Text { get; } = text;
    public float Number { get; } = number;
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

    /// <summary>
    /// Hard playback ceilings mirrored by the client. The client rejects downloads above them
    /// no matter what the server cvars say, so the server must not admit anything bigger either:
    /// such a song would be stored and then stay silent for everyone.
    /// </summary>
    public const int HardMaxSongBytes = 8_000_000;
    public const int HardMaxSongSeconds = 600;
    public const long HardMaxDecodedBytes = 100_000_000;
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
