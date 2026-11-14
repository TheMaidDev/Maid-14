using Content.Shared._Maid.Mood;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     A rake trap. Deployed on the tile in front of the user; while armed, whoever steps on it
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
    ///     Who deployed this rake, server-side only: the installer steps on their own trap
    ///     freely. Cleared when the rake disarms, re-set on the next deploy.
    /// </summary>
    [DataField]
    public EntityUid? Installer;

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
