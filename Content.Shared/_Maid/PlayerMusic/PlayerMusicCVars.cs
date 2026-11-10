using Robust.Shared.Configuration;

namespace Content.Shared._Maid.PlayerMusic;

[CVarDefs]
public sealed class PlayerMusicCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("maidmusic.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Max size of one uploaded song in bytes.
    /// </summary>
    public static readonly CVarDef<int> MaxSongBytes =
        CVarDef.Create("maidmusic.max_song_bytes", 3_500_000, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Max length of one song in seconds.
    /// </summary>
    public static readonly CVarDef<int> MaxSongSeconds =
        CVarDef.Create("maidmusic.max_song_seconds", 240, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Max size of one song after decoding (the engine keeps all of it in memory while it is cached).
    /// Limits the memory used on clients regardless of sample rate and channel count.
    /// </summary>
    public static readonly CVarDef<int> MaxDecodedBytes =
        CVarDef.Create("maidmusic.max_decoded_bytes", 48_000_000, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// How many different songs one player can record per round.
    /// </summary>
    public static readonly CVarDef<int> MaxSongsPerPlayer =
        CVarDef.Create("maidmusic.max_songs_per_player", 6, CVar.SERVERONLY);

    /// <summary>
    /// Total size of all songs kept in server memory per round.
    /// </summary>
    public static readonly CVarDef<int> MaxTotalBytes =
        CVarDef.Create("maidmusic.max_total_bytes", 64_000_000, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between two recordings by the same player.
    /// </summary>
    public static readonly CVarDef<float> UploadCooldown =
        CVarDef.Create("maidmusic.upload_cooldown", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Only players within this distance of a playing boombox may download its song.
    /// </summary>
    public static readonly CVarDef<float> ListenRange =
        CVarDef.Create("maidmusic.listen_range", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Player's own volume setting for boomboxes and walkmans (1 = 100%).
    /// </summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("maidmusic.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
