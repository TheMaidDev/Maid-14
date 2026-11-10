using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.PlayerMusic;

/// <summary>
/// One recorded song on a cassette. The audio itself is stored by the server.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct PlayerMusicTrack(string Id, string Name);

/// <summary>
/// Portable player that plays a song recorded on a <see cref="PlayerMusicCassetteComponent"/>.
/// The server only replicates "which song, since when"; clients fetch the audio file on demand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PlayerMusicBoomboxComponent : Component
{
    /// <summary>
    /// Item slot id (must match the ItemSlots / ContainerContainer id in YAML).
    /// </summary>
    public const string CassetteSlotId = "cassette_slot";

    /// <summary>
    /// Id (hash) of the song that is currently playing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? SongId;

    [DataField, AutoNetworkedField]
    public string? SongName;

    /// <summary>
    /// Server time when playback started. Null = stopped.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? StartedAt;

    /// <summary>
    /// Loop the current song (on by default, like in the old jukebox).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Repeat = true;

    // UI info about the inserted cassette
    [DataField, AutoNetworkedField]
    public bool HasCassette;

    [DataField, AutoNetworkedField]
    public int CassetteMaxTracks;

    [AutoNetworkedField]
    public List<PlayerMusicTrack> CassetteTracks = new();

    [DataField]
    public float MaxDistance = 15f;

    [DataField]
    public float Volume = -4f;
}

/// <summary>
/// A cassette that can hold several player-recorded songs. It only stores song ids and names,
/// the audio itself is kept by the server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlayerMusicCassetteComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MaxTracks = 3;

    [AutoNetworkedField]
    public List<PlayerMusicTrack> Tracks = new();
}
