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
        CVarDef.Create("tts.volume", 1f, CVar.CLIENTONLY);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    #endregion

    #region Audio

    /// <summary>
    /// Whether tile-based sound path muffling is enabled.
    /// </summary>
    public static readonly CVarDef<bool> SoundPathEnabled =
        CVarDef.Create("maidsound.path_enabled", true, CVar.CLIENTONLY);

    /// <summary>
    /// Maximum radius in tiles to search sound paths in.
    /// </summary>
    public static readonly CVarDef<int> SoundPathRadius =
        CVarDef.Create("maidsound.path_radius", 25, CVar.CLIENTONLY);

    /// <summary>
    /// Occlusion added per unit of sound path weight.
    /// </summary>
    public static readonly CVarDef<float> SoundPathOcclusionPerWeight =
        CVarDef.Create("maidsound.path_occlusion_per_weight", 15f, CVar.CLIENTONLY);

    /// <summary>
    /// Volume loss in decibels per tile of sound path length.
    /// </summary>
    public static readonly CVarDef<float> SoundPathLossDbPerTile =
        CVarDef.Create("maidsound.path_loss_db_per_tile", 1.5f, CVar.CLIENTONLY);

    /// <summary>
    /// Volume loss in decibels per unit of sound path occlusion.
    /// </summary>
    public static readonly CVarDef<float> SoundPathOcclusionLossDb =
        CVarDef.Create("maidsound.path_occlusion_loss_db", 15f, CVar.CLIENTONLY);

    /// <summary>
    /// Occlusion faded per tile of sound path past the last blocker.
    /// </summary>
    public static readonly CVarDef<float> SoundPathOcclusionDecayPerTile =
        CVarDef.Create("maidsound.path_occlusion_decay_per_tile", 0.3f, CVar.CLIENTONLY);

    /// <summary>
    /// Sound path weight of opaque tiles.
    /// </summary>
    public static readonly CVarDef<float> SoundPathWeightOpaque =
        CVarDef.Create("maidsound.weight_opaque", 8f, CVar.CLIENTONLY);

    /// <summary>
    /// Sound path weight of glass.
    /// </summary>
    public static readonly CVarDef<float> SoundPathWeightGlass =
        CVarDef.Create("maidsound.weight_glass", 0.3f, CVar.CLIENTONLY);

    /// <summary>
    /// Lifetime in seconds of cached sound paths.
    /// </summary>
    public static readonly CVarDef<float> SoundPathCacheTtl =
        CVarDef.Create("maidsound.path_cache_ttl", 2f, CVar.CLIENTONLY);

    /// <summary>
    /// Fraction of the gap between current and target filter value closed per audio tick at 30 TPS; 1 disables smoothing.
    /// </summary>
    public static readonly CVarDef<float> SoundPathSmoothing =
        CVarDef.Create("maidsound.path_smoothing", 0.15f, CVar.CLIENTONLY);

    /// <summary>
    /// How strongly detour routes prefer clean tiles over short ones: blocker occlusion is scaled by this on the detour pass; 0 disables detours.
    /// </summary>
    public static readonly CVarDef<float> SoundPathDetourWeight =
        CVarDef.Create("maidsound.path_detour_weight", 6f, CVar.CLIENTONLY);

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

    /// <summary>
    ///     If true doesn't allow to FTL on trade map while anyone alive is on shuttle
    /// </summary>
    public static readonly CVarDef<bool> DenyAliveTradeFTL =
        CVarDef.Create("maid.deny_alive_trade_ftl", false, CVar.SERVERONLY);

    ///     Whether IC messages get their slang words automatically replaced with in-character ones.
    /// </summary>
    public static readonly CVarDef<bool> ChatSlangFilter =
        CVarDef.Create("ic.slang_filter", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<bool> AhelpHistory =
        CVarDef.Create("ahelp.history", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> AhelpMaxMessageLength =
        CVarDef.Create("ahelp.max_message_length", 2000, CVar.SERVER | CVar.REPLICATED);

    #endregion

    #region Economy

    /// <summary>
    /// How often salaries are paid to crew bank accounts.
    /// </summary>
    public static readonly CVarDef<float> EconomySalaryInterval =
        CVarDef.Create("maid.economy.salary_interval", 1200f, CVar.SERVERONLY);

    /// <summary>
    /// Money every crew member starts the round with, on top of one salary.
    /// </summary>
    public static readonly CVarDef<int> EconomyStartingBalance =
        CVarDef.Create("maid.economy.starting_balance", 100, CVar.SERVERONLY);

    /// <summary>
    /// Multiplier applied to the estimated price of items sold by vending machines.
    /// </summary>
    public static readonly CVarDef<float> EconomyVendingPriceMultiplier =
        CVarDef.Create("maid.economy.vending_price_multiplier", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Price of vending machine items that have no estimated price.
    /// </summary>
    public static readonly CVarDef<int> EconomyVendingDefaultPrice =
        CVarDef.Create("maid.economy.vending_default_price", 15, CVar.SERVERONLY);

    #endregion

    #region End of round stats

    /// <summary>
    ///     How many slips are needed to show the slip count and the top slipper in the round end summary.
    /// </summary>
    public static readonly CVarDef<int> SlippedCountThreshold =
        CVarDef.Create("eorstats.slippedcount_threshold", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Show a line in the round end summary when nobody slipped at all.
    /// </summary>
    public static readonly CVarDef<bool> SlippedCountDisplayNone =
        CVarDef.Create("eorstats.slippedcount_displaynone", true, CVar.SERVERONLY);

    /// <summary>
    ///     Show who slipped the most in the round end summary.
    /// </summary>
    public static readonly CVarDef<bool> SlippedCountTopSlipper =
        CVarDef.Create("eorstats.slippedcount_topslipper", true, CVar.SERVERONLY);

    #endregion

    #region Client preferences

    /// <summary>
    ///     Show the give item indicator next to the cursor while offering an item.
    /// </summary>
    public static readonly CVarDef<bool> OfferModeIndicatorsPointShow =
        CVarDef.Create("maid.offer_mode_indicators_point_show", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Draw the glow around light fixtures.
    /// </summary>
    public static readonly CVarDef<bool> EnableLightsGlowing =
        CVarDef.Create("maid.enable_lights_glowing", false, CVar.CLIENTONLY);

    /// <summary>
    ///     Draw a film grain effect over the screen.
    /// </summary>
    public static readonly CVarDef<bool> GrainEnabled =
        CVarDef.Create("maid.grain_enabled", false, CVar.CLIENTONLY);

    /// <summary>
    ///     Film grain strength multiplier, 1 is the default look.
    /// </summary>
    public static readonly CVarDef<float> GrainStrength =
        CVarDef.Create("maid.grain_strength", 1f, CVar.CLIENTONLY);

    /// <summary>
    ///     Automatically get up when the knockdown ends.
    /// </summary>
    public static readonly CVarDef<bool> AutoGetUp =
        CVarDef.Create("maid.auto_get_up", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    ///     If true, the look up key has to be held to aim through a telescope; otherwise it toggles.
    /// </summary>
    public static readonly CVarDef<bool> HoldLookUp =
        CVarDef.Create("maid.hold_look_up", false, CVar.CLIENTONLY);

    /// <summary>
    ///     If true, an admin's dead chat messages are sent as admin messages with their username;
    ///     otherwise they are sent like a regular player's, with the current character name.
    /// </summary>
    public static readonly CVarDef<bool> DeadChatAdmin =
        CVarDef.Create("maid.admin.dead_chat_admin", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Volume of the ahelp sound for admins, as a gain from 0 to 1.
    /// </summary>
    public static readonly CVarDef<float> AhelpVolume =
        CVarDef.Create("maid.admin.ahelp_volume", 1f, CVar.CLIENTONLY);

    #endregion

    #region Salvage expeditions

    /// <summary>
    ///     Cooldown before new expeditions are offered after a failed mission.
    /// </summary>
    public static readonly CVarDef<float> SalvageExpeditionFailedCooldown =
        CVarDef.Create("salvage.expedition_failed_cooldown", 900f, CVar.REPLICATED);

    #endregion

    #region Discord feeds

    /// <summary>
    ///     URL of the Discord webhook that receives admin chat messages. If left empty, disables the feed.
    /// </summary>
    public static readonly CVarDef<string> DiscordAdminChatWebhook =
        CVarDef.Create("discord.admin_chat_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     URL of the Discord webhook that receives notifications about players joining for the first time.
    ///     If left empty, disables the feed.
    /// </summary>
    public static readonly CVarDef<string> DiscordNewPlayerWebhook =
        CVarDef.Create("discord.new_player_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     The new player notification gets an extra warning when the account shares a HWID (according to the auth server)
    ///     with other accounts and has played fewer than this many minutes. 0 disables the warning.
    /// </summary>
    public static readonly CVarDef<int> DiscordNewPlayerAltPlaytimeMinutes =
        CVarDef.Create("discord.new_player_alt_playtime_minutes", 30, CVar.SERVERONLY);

    #endregion
}
