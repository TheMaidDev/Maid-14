using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Maid.Extraction;

public sealed partial class LaunchDropPodActionEvent : InstantActionEvent;

/// <summary>
/// Tells clients to play the lift-off animation of a drop pod that is being deleted.
/// </summary>
[Serializable, NetSerializable]
public sealed class DropPodLaunchAnimationEvent(NetCoordinates coordinates, float duration, List<SpriteSpecifier> layers, Vector2 offset) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly float Duration = duration;
    public readonly List<SpriteSpecifier> Layers = layers;

    /// <summary>
    /// Sprite offset of the pod, the lift-off starts from it.
    /// </summary>
    public readonly Vector2 Offset = offset;
}

/// <summary>
/// Light of the pod: red while locked, green once the code is entered, blue when sealed for launch, yellow on lift-off.
/// </summary>
[Serializable, NetSerializable]
public enum DropPodGlow : byte
{
    Locked,
    Unlocked,
    Sealed,
    Launching,
}

[Serializable, NetSerializable]
public enum DropPodUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum DropPodStatus : byte
{
    Locked,
    WrongCode,
    Unlocked,
    Launching,
}

[Serializable, NetSerializable]
public sealed class DropPodUiState(DropPodStatus status, int enteredLength, int codeLength) : BoundUserInterfaceState
{
    public readonly DropPodStatus Status = status;
    public readonly int EnteredLength = enteredLength;
    public readonly int CodeLength = codeLength;
}

[Serializable, NetSerializable]
public sealed class DropPodKeypadMessage(int value) : BoundUserInterfaceMessage
{
    public readonly int Value = value;
}

[Serializable, NetSerializable]
public sealed class DropPodKeypadClearMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class DropPodKeypadEnterMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum ExtractionDeviceUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ExtractionObjectiveEntry(string title, bool completed, bool required)
{
    public readonly string Title = title;
    public readonly bool Completed = completed;
    public readonly bool Required = required;
}

/// <summary>
/// Everything the extraction device shows. Texts that depend on server-only data come already localized.
/// Sent as a message to the owner only: BUI states are networked to every client that sees the device,
/// and this one carries the pod code, the objectives and the zone locations.
/// </summary>
[Serializable, NetSerializable]
public sealed class ExtractionDeviceUiState(
    bool unlocked,
    List<ExtractionObjectiveEntry> objectives,
    string progress,
    int rerollsLeft,
    string? code,
    string zone,
    string pod,
    bool canRequestZone,
    bool canCallPod) : BoundUserInterfaceMessage
{
    public readonly bool Unlocked = unlocked;
    public readonly List<ExtractionObjectiveEntry> Objectives = objectives;
    public readonly string Progress = progress;
    public readonly int RerollsLeft = rerollsLeft;
    public readonly string? Code = code;
    public readonly string Zone = zone;
    public readonly string Pod = pod;
    public readonly bool CanRequestZone = canRequestZone;
    public readonly bool CanCallPod = canCallPod;
}

[Serializable, NetSerializable]
public sealed class ExtractionDeviceRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ExtractionDeviceRequestZoneMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ExtractionDeviceCallPodMessage : BoundUserInterfaceMessage;
