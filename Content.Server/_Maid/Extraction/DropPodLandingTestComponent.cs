using Content.Shared._Maid.Extraction;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Testing marker: drops an empty, unlocked drop pod on its tile after a short delay, then deletes itself.
/// Used to check the impact sound, screen shake and decal without going through extraction.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class DropPodLandingTestComponent : Component
{
    [DataField]
    public DropPodLanding Landing = new() { CrushLiving = false };

    /// <summary>
    /// Time to step back after spawning the marker.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3);

    [DataField, AutoPausedField]
    public TimeSpan LandAt;
}
