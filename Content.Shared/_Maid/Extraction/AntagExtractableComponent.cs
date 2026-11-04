using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Lets an antagonist leave the round once enough objectives are done: they get an extraction zone,
/// call a drop pod into it, unlock it with their code and launch it from inside.
/// Station items they carry are left behind, their own gear leaves with them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AntagExtractableComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>
    /// Texts for this antagonist, override them in the prototype for its own flavour.
    /// </summary>
    [DataField]
    public ExtractionMessages Messages = new();

    /// <summary>
    /// Items the antagonist carries that are not their own gear drop at the pod instead of leaving with them.
    /// Off by default: the antagonist can take anything. People they carry always stay on the station.
    /// </summary>
    [DataField]
    public bool LeaveStationItems;

    /// <summary>
    /// How often objectives are checked for unlocking.
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether <see cref="Threshold"/> is a percent of counted objectives or a flat number of them.
    /// </summary>
    [DataField]
    public ExtractionThresholdMode ThresholdMode = ExtractionThresholdMode.Percent;

    /// <summary>
    /// Percent (0-100) or count of counted objectives that must be completed.
    /// </summary>
    [DataField]
    public float Threshold = 100f;

    /// <summary>
    /// Objectives that must all be completed. They also count toward <see cref="Threshold"/>.
    /// </summary>
    [DataField]
    public List<EntProtoId> RequiredObjectives = new();

    /// <summary>
    /// Objectives that count toward <see cref="Threshold"/>. Objectives in neither list are ignored.
    /// With both lists empty every objective except survival ones counts.
    /// </summary>
    [DataField]
    public List<EntProtoId> OptionalObjectives = new();

    /// <summary>
    /// How many times a zone can be requested: the first request plus re-rolls.
    /// </summary>
    [DataField]
    public int MaxRequests = 2;

    [DataField, AutoNetworkedField]
    public int RequestsUsed;

    [DataField, AutoNetworkedField]
    public bool Unlocked;

    /// <summary>
    /// Unlocked by an admin: objectives are not re-checked later.
    /// </summary>
    [DataField]
    public bool ForceUnlocked;

    /// <summary>
    /// Current extraction zones. A new set is given only when all of them are destroyed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Zones = new();

    /// <summary>
    /// How many zones one request designates.
    /// </summary>
    [DataField]
    public int ZoneCount = 3;

    [DataField]
    public EntProtoId ZonePrototype = "ExtractionZoneMarker";

    /// <summary>
    /// Component names of the entities zones are placed at, on the largest grid of the station, in order of preference.
    /// When there are not enough of the first kind the next one is used, then random tiles inside the station.
    /// </summary>
    [DataField]
    public List<string> ZoneAnchorComponents = new() { "Holopad", "NavMapBeacon" };

    /// <summary>
    /// Anchor prototypes the zone never spawns at, e.g. the holopad inside the AI core.
    /// </summary>
    [DataField]
    public List<EntProtoId> ZoneAnchorBlacklist = new() { "HolopadAiCore" };

    /// <summary>
    /// Device with the extraction controls, put into the backpack or a free pocket. Null gives none.
    /// </summary>
    [DataField]
    public EntProtoId? DevicePrototype = "ExtractionDevice";

    [DataField]
    public EntityUid? Device;

    /// <summary>
    /// The device was handed out once already, a lost one is not replaced.
    /// </summary>
    [DataField]
    public bool DeviceGiven;

    /// <summary>
    /// The pod can be called only once. Only the admin reset command allows another call.
    /// </summary>
    [DataField]
    public bool PodCalled;

    /// <summary>
    /// Code that unlocks the drop pod, given to the player on unlock. Server only.
    /// </summary>
    [DataField]
    public string Code = string.Empty;

    /// <summary>
    /// Digits in the pod code, 1 to 9.
    /// </summary>
    [DataField]
    public int CodeLength = 6;

    /// <summary>
    /// How the pod lands.
    /// </summary>
    [DataField]
    public DropPodLanding Landing = new();

    /// <summary>
    /// Pod that already landed for this antagonist.
    /// </summary>
    [DataField]
    public EntityUid? Pod;

    /// <summary>
    /// Where the called pod is going to land, while it is on its way.
    /// </summary>
    [DataField]
    public EntityCoordinates? PodTarget;

    [DataField]
    public TimeSpan PodLandAt;

    [DataField]
    public TimeSpan? PodAnnounceAt;

    [DataField]
    public TimeSpan PodFlightTime = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan PodAnnounceDelayMin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The station hears about the incoming pod at a random moment between the min and max delay after the call.
    /// </summary>
    [DataField]
    public TimeSpan PodAnnounceDelayMax = TimeSpan.FromSeconds(20);

    /// <summary>
    /// When the antagonist arrived, for the round end summary.
    /// </summary>
    [DataField]
    public TimeSpan ArrivalTime;

    /// <summary>
    /// Already left the round, the body waits on the extraction map.
    /// </summary>
    [DataField]
    public bool Extracted;

    [ViewVariables]
    public TimeSpan NextCheck;
}

public enum ExtractionThresholdMode : byte
{
    Percent,
    Count,
}
