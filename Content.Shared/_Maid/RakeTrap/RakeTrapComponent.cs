using Content.Shared._Maid.Mood;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     A rake trap. Deployed on the tile in front of the user; while armed, any humanoid
///     (HumanoidAppearanceComponent: humans, IPCs and the other species) that steps on it
///     gets whacked in the head, after which the rake disarms itself until deployed again.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class RakeTrapComponent : Component
{
    /// <summary>Whether the trap is set and can trigger.</summary>
    [ViewVariables, AutoNetworkedField]
    public bool Armed;

    /// <summary>
    ///     Who deployed this rake, server-side only: during the grace window after deploying
    ///     (see <see cref="InstallerGrace"/>) the installer steps over their own trap safely.
    ///     Cleared when the rake disarms or fires, re-set on the next deploy.
    /// </summary>
    [DataField]
    public EntityUid? Installer;

    /// <summary>When the trap was last deployed, server-side only: the installer grace window starts here.</summary>
    [DataField]
    public TimeSpan? DeployedAt;

    /// <summary>How long after deploying the installer steps over their own trap safely — not forever.</summary>
    [DataField]
    public TimeSpan InstallerGrace = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Set when the trap fires, replicated so clients can play the whack animation.
    ///     Null when the trap is not currently springing.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? HitAt;

    /// <summary>How long the victim stays knocked down.</summary>
    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(5);

    /// <summary>How long the victim's audio stays muffled.</summary>
    [DataField]
    public TimeSpan MuffleTime = TimeSpan.FromSeconds(5);

    /// <summary>Master gain applied to the victim's client while muffled.</summary>
    [DataField]
    public float MuffleGain = 0.3f;

    /// <summary>
    ///     Minimum speed (m/s) the victim must be moving at for the trap to go off, and they
    ///     must be on their feet: lying or crawling bodies slide over the rake harmlessly.
    /// </summary>
    [DataField]
    public float MinimumTriggerSpeed = 1.5f;

    /// <summary>How long disarming an armed trap by clicking it takes.</summary>
    [DataField]
    public TimeSpan DisarmTime = TimeSpan.FromSeconds(2);

    /// <summary>How long the whack animation plays before the sprite resets.</summary>
    [DataField]
    public TimeSpan ResetVisualTime = TimeSpan.FromSeconds(0.9);

    [DataField]
    public DamageSpecifier Damage = new() { DamageDict = { ["Blunt"] = 30 } };

    [DataField]
    public ProtoId<MoodEffectPrototype> Mood = "MobRaked";

    [DataField]
    public SoundSpecifier DeploySound = new SoundPathSpecifier("/Audio/Weapons/click.ogg");

    /// <summary>Played when the rake springs up off the floor.</summary>
    [DataField]
    public SoundSpecifier CockSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");

    /// <summary>Played when the rake smacks the victim.</summary>
    [DataField]
    public SoundSpecifier HitSound = new SoundCollectionSpecifier("WoodDestroyHeavy");

    /// <summary>Played when the armed rake is disarmed.</summary>
    [DataField]
    public SoundSpecifier DisarmSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");
}
