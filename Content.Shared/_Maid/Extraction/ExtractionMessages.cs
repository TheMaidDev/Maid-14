namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Texts an extractable antagonist and the station get. Each antagonist can override any of them for its own flavour,
/// the defaults are neutral.
/// </summary>
[DataDefinition]
public sealed partial class ExtractionMessages
{
    /// <summary>
    /// Popup pointing the player to the chat whenever a message is sent.
    /// </summary>
    [DataField]
    public LocId NotifyPopup = "extraction-notify-popup";

    /// <summary>
    /// Objectives done. Gets <c>$code</c>.
    /// </summary>
    [DataField]
    public LocId Unlocked = "extraction-unlocked";

    /// <summary>
    /// Unlocked by an admin. Gets <c>$code</c>.
    /// </summary>
    [DataField]
    public LocId UnlockedForced = "extraction-unlocked-forced";

    /// <summary>
    /// Gets <c>$location</c> and <c>$left</c>.
    /// </summary>
    [DataField]
    public LocId ZoneDesignated = "extraction-zone-designated";

    /// <summary>
    /// No re-requests left while a zone exists. Gets <c>$location</c>.
    /// </summary>
    [DataField]
    public LocId NoRequestsZone = "extraction-no-requests-zone";

    [DataField]
    public LocId NoRequests = "extraction-no-requests";

    [DataField]
    public LocId NoZone = "extraction-no-zone";

    /// <summary>
    /// The zone was destroyed and replaced. Gets <c>$location</c>.
    /// </summary>
    [DataField]
    public LocId ZoneLost = "extraction-zone-lost";

    [DataField]
    public LocId ZoneLostNone = "extraction-zone-lost-none";

    [DataField]
    public LocId ZoneReset = "extraction-zone-reset";

    /// <summary>
    /// After reconnecting, no zone yet. Gets <c>$code</c>.
    /// </summary>
    [DataField]
    public LocId Reminder = "extraction-reminder";

    /// <summary>
    /// After reconnecting with a zone. Gets <c>$location</c> and <c>$code</c>.
    /// </summary>
    [DataField]
    public LocId ReminderZone = "extraction-reminder-zone";

    [DataField]
    public LocId NotInZone = "extraction-not-in-zone";

    /// <summary>
    /// Location of a zone placed at a random tile because the station has no anchors.
    /// Gets <c>$location</c> (nearest beacon), <c>$x</c> and <c>$y</c>.
    /// </summary>
    [DataField]
    public LocId ZoneCoordinates = "extraction-zone-coordinates";

    /// <summary>
    /// Description of the zone marker. Gets <c>$name</c>.
    /// </summary>
    [DataField]
    public LocId ZoneMarkerDescription = "extraction-zone-marker-desc";

    /// <summary>
    /// Gets <c>$seconds</c>.
    /// </summary>
    [DataField]
    public LocId PodCalled = "extraction-pod-called";

    /// <summary>
    /// Gets <c>$code</c> and <c>$minutes</c> until the pod leaves on its own.
    /// </summary>
    [DataField]
    public LocId PodLanded = "extraction-pod-landed";

    /// <summary>
    /// The pod is about to leave on its own. Gets <c>$seconds</c>.
    /// </summary>
    [DataField]
    public LocId PodLeavingSoon = "extraction-pod-leaving-soon";

    /// <summary>
    /// The pod left on its own without the antagonist.
    /// </summary>
    [DataField]
    public LocId PodLeftWithout = "extraction-pod-left-without";

    /// <summary>
    /// The landing failed, the pod can be called again. Gets <c>$seconds</c>.
    /// </summary>
    [DataField]
    public LocId PodLost = "extraction-pod-lost";

    [DataField]
    public LocId PodDestroyed = "extraction-pod-destroyed";

    /// <summary>
    /// Station announcement while the pod is on its way. Gets <c>$location</c> of the landing point.
    /// </summary>
    [DataField]
    public LocId PodAnnouncement = "extraction-pod-announcement";

    [DataField]
    public LocId AnnouncementSender = "extraction-announcement-sender";

    [DataField]
    public Color AnnouncementColor = Color.Gold;

    /// <summary>
    /// Round end summary line. Gets <c>$name</c> and <c>$minutes</c>.
    /// </summary>
    [DataField]
    public LocId RoundEnd = "extraction-round-end";
}
