using Robust.Shared.GameStates;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     Short-lived "hit in the face" daze on a rake victim. Drives the client screen overlay
///     (impact flash + dizzy wobble), see <c>RakeHitOverlay</c>. Removed by the server after
///     <see cref="Duration"/>.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class RakeDazedComponent : Component
{
    /// <summary>When the rake hit landed; the overlay derives the flash/wobble progress from it.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? HitAt;

    /// <summary>How long the screen daze lasts.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1.5);
}
