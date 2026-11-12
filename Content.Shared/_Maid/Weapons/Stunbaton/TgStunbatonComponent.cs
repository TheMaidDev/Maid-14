using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Maid.Weapons.Stunbaton;

/// <summary>
/// Security stun baton combat from TG: no swing at air while on, stun cooldown, delayed knockdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class TgStunbatonComponent : Component
{
    /// <summary>
    /// Child prototypes that should not use this behavior set this to false.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Stamina applied by a thrown baton. Melee uses <c>StaminaDamageOnHit</c>.
    /// </summary>
    [DataField]
    public float ThrowStaminaDamage = 60f;

    /// <summary>
    /// Seconds before the knockdown, matching TG <c>apply_stun_effect_end</c>.
    /// </summary>
    [DataField]
    public float KnockdownDelay = 2f;

    /// <summary>
    /// Knockdown length once the delay elapses. TG security baton uses 5 seconds.
    /// </summary>
    [DataField]
    public float KnockdownTime = 5f;

    /// <summary>
    /// Seconds before this baton can stun again, and before the same user can stun the same target.
    /// </summary>
    [DataField]
    public float Cooldown = 2.5f;

    /// <summary>
    /// Chance that an active thrown baton stuns whatever it hits.
    /// </summary>
    [DataField]
    public float ThrowStunChance = 0.35f;

    /// <summary>
    /// Visual shake after a hit. TG uses 40 seconds, which is far too long for this sprite jitter.
    /// </summary>
    [DataField]
    public float JitterTime = 4f;

    [DataField]
    public float StutterTime = 16f;

    /// <summary>
    /// <see cref="Robust.Shared.Timing.IGameTiming.CurTime"/> when the baton may stun again.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextStun;

    /// <summary>
    /// Wielder of the swing currently being resolved. Not networked; both sides set it before stamina is applied.
    /// </summary>
    [NonSerialized]
    public EntityUid? SwingUser;
}

/// <summary>
/// Raised on the baton after a stun connects, on whichever side resolved the swing.
/// </summary>
[ByRefEvent]
public readonly record struct TgStunbatonStunEvent(EntityUid Target);
