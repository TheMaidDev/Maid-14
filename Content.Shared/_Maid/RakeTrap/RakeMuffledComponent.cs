using Robust.Shared.GameStates;

namespace Content.Shared._Maid.RakeTrap;

/// <summary>
///     Marks a mob whose client audio is muffled (e.g. stepped on a rake). The client ducks
///     its master gain while the component is present, see <c>RakeMuffleSystem</c>.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class RakeMuffledComponent : Component
{
    /// <summary>Master gain applied on the victim's client while the component is present.</summary>
    [DataField, AutoNetworkedField]
    public float Gain = 0.3f;
}
