using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Extraction drop pod: an entity storage locked with the extractee's code.
/// Once the extractee is inside, they can launch it and leave the round.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DropPodComponent : Component
{
    /// <summary>
    /// The only one allowed inside. Networked so the client predicts insertion the same way;
    /// cleared when the extractee goes away so it never points to a deleted entity.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? Extractee;

    /// <summary>
    /// When the pod hit the station, the client plays the landing animation right after it.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public TimeSpan LandedAt;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public string LandingState = "landing";

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public string BaseState = "base";

    /// <summary>
    /// Length of the landing animation: 0.5 s first frame, then 8 frames of 0.05 s.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan LandingAnimationLength = TimeSpan.FromSeconds(0.9);

    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Locked = true;

    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Launching;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan LaunchDelay = TimeSpan.FromSeconds(15);

    [DataField, ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan LaunchAt;

    /// <summary>
    /// A landed pod leaves the station on its own after this long, with or without anyone inside.
    /// Zero keeps it forever.
    /// </summary>
    [DataField]
    public TimeSpan AutoLaunchDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long before leaving on its own the extractee is warned.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan AutoLaunchWarning = TimeSpan.FromMinutes(1);

    [DataField, ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan? AutoLaunchAt;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool AutoLaunchWarned;

    /// <summary>
    /// Seconds of the countdown shown to the occupant as a popup.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public List<int> CountdownPopups = new() { 10, 5, 4, 3, 2, 1 };

    /// <summary>
    /// Last countdown second already announced to the occupant.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public int LastCountdownSecond;

    /// <summary>
    /// Length of the lift-off animation.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float LaunchAnimationDuration = 2f;

    /// <summary>
    /// Sprite layers of the pod flying away, the real pod is deleted when the animation starts.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public List<SpriteSpecifier> LaunchSprite = new()
    {
        new SpriteSpecifier.Rsi(new ResPath("_Maid/Structures/Storage/drop_pod.rsi"), "base"),
        new SpriteSpecifier.Rsi(new ResPath("_Maid/Structures/Storage/drop_pod.rsi"), "door"),
    };

    /// <summary>
    /// Sprite offset of the pod, so the lift-off starts where the pod is drawn.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Vector2 LaunchSpriteOffset = new(0f, 0.25f);

    /// <summary>
    /// Point light colour for each glow state.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<DropPodGlow, Color> GlowColors = new()
    {
        { DropPodGlow.Locked, Color.FromHex("#ff3b3b") },
        { DropPodGlow.Unlocked, Color.FromHex("#3bff6b") },
        { DropPodGlow.Sealed, Color.FromHex("#3b8bff") },
        { DropPodGlow.Launching, Color.FromHex("#ffd23b") },
    };

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public EntProtoId LaunchAction = "ActionLaunchDropPod";

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? LaunchActionEntity;

    /// <summary>
    /// Rubble decal left where the pod hit.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public string LandingDecal = "DropPodRubble";

    /// <summary>
    /// Decal position from the corner of the landing tile. Decals are drawn from their bottom left corner,
    /// so a 64x64 decal is moved half a tile back to sit centered, and up like the pod sprite.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Vector2 LandingDecalOffset = new(-0.5f, -0.25f);

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier LaunchSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    /// <summary>
    /// Played every second of the launch countdown. None yet, the sound is picked later.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier? CountdownSound;

    /// <summary>
    /// Engine sound started with the launch countdown and stopped if the launch is cancelled.
    /// Loops only if its params say so.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier? EngineSound;

    [ViewVariables]
    public EntityUid? EngineSoundStream;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier KeypadSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier WrongCodeSound = new SoundPathSpecifier("/Audio/Machines/Nuke/angry_beep.ogg");

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier UnlockSound = new SoundPathSpecifier("/Audio/Machines/Nuke/confirm_beep.ogg");

    /// <summary>
    /// Digits typed on the keypad so far. Server only.
    /// </summary>
    [ViewVariables]
    public string EnteredCode = string.Empty;

    [ViewVariables]
    public bool WrongCode;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public LocId InsideMessage = "extraction-pod-inside";

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public LocId NotInsideMessage = "extraction-pod-not-inside";

    /// <summary>
    /// Gets <c>$seconds</c>.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public LocId LaunchingMessage = "extraction-pod-launching";

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public LocId LaunchCancelledMessage = "extraction-pod-launch-cancelled";

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public LocId SealedMessage = "extraction-pod-sealed";
}
