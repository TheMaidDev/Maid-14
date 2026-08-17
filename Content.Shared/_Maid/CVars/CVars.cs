using Content.Shared._Maid.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Shared._Maid.CVars;

[CVarDefs]
public sealed class MaidCVars
{
    #region TTS

    /// <summary>
    /// Whether TTS is enabled on the server.
    /// </summary>
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", "", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Amount of seconds before timeout for API
    /// </summary>
    public static readonly CVarDef<int> TTSApiTimeout =
        CVarDef.Create("tts.api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    #endregion

    #region Misc

    /// <summary>
    ///     Are height/width sliders enabled
    /// </summary>
    public static readonly CVarDef<bool> HeightSliders =
        CVarDef.Create("maid.height_sliders_enabled", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Controls detailed examine panel style.
    /// </summary>
    public static readonly CVarDef<int> DetailedExamineStyle =
        CVarDef.Create("maid.detailed_examine_style", (int)DetailedExamineType.Fancy, CVar.ARCHIVE | CVar.REPLICATED | CVar.CLIENT);

    /// <summary>
    ///     Do generate Ert map on round start or not
    /// </summary>
    public static readonly CVarDef<bool> LoadErtMap =
        CVarDef.Create("maid.load_ert_map", true, CVar.SERVERONLY);

    /// <summary>
    ///     Should players get a random weapon on roundend
    /// </summary>
    public static readonly CVarDef<bool> RoundEndWeapons =
        CVarDef.Create("maid.round_end_weapons_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enable collecting adaptive rule balancing statistics.
    /// </summary>
    public static readonly CVarDef<bool> AdaptiveStatistics =
        CVarDef.Create("maid.adaptive_statistics", false, CVar.SERVERONLY);

    #endregion
}
