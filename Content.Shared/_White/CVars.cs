using Content.Shared._White.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Shared._White;

[CVarDefs]
public sealed class WhiteCVars
{
    public static readonly CVarDef<float> GhostRespawnTime =
        CVarDef.Create("ghost.respawn_time", 15f, CVar.SERVERONLY);

    public static readonly CVarDef<int> GhostRespawnMaxPlayers =
        CVarDef.Create("ghost.respawn_max_players", 40, CVar.SERVERONLY);

    public static readonly CVarDef<EmotesMenuType> EmotesMenuStyle =
        CVarDef.Create("interface.emotes_menu_style", EmotesMenuType.Window, CVar.CLIENT | CVar.ARCHIVE);

    /*
     * Bullet trails
     */

    /// <summary>
    /// Whether concealable trails (bullet tracers) get rendered at all.
    /// </summary>
    public static readonly CVarDef<bool> ShowTrails =
        CVarDef.Create("white.show_trails", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /*
     * Explosion shockwave
     */

    /// <summary>
    /// Whether the screen-distorting shockwave overlay is drawn on explosions.
    /// </summary>
    public static readonly CVarDef<bool> ShowExplosionShockWave =
        CVarDef.Create("white.show_explosion_shockwave", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether explosions spawn a shockwave effect entity at their epicenter.
    /// </summary>
    public static readonly CVarDef<bool> ExplosionShockWaveEnabled =
        CVarDef.Create("white.explosion_shockwave_enabled", true, CVar.SERVERONLY);

    /*
     * Reputation
     */

    /// <summary>
    /// Whether the reputation ("rating") system is active. Disables round-end gain when false.
    /// </summary>
    public static readonly CVarDef<bool> ReputationEnabled =
        CVarDef.Create("reputation.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether a player's reputation is shown next to their name in OOC.
    /// </summary>
    public static readonly CVarDef<bool> ReputationShowInOoc =
        CVarDef.Create("reputation.show_in_ooc", true, CVar.SERVERONLY);
}
