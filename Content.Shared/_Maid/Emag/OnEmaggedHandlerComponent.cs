using Content.Shared.Emag.Systems;

namespace Content.Shared._Maid.Emag;

/// <summary>
/// Makes an entity emaggable without any custom logic: the emag succeeds once per <see cref="Flag"/> flag.
/// React to the result by listening for <see cref="Content.Shared.Emag.Components.EmaggedComponent"/> being added.
/// </summary>
[RegisterComponent]
public sealed partial class OnEmaggedHandlerComponent : Component
{
    /// <summary>
    /// Emag flag this entity accepts.
    /// </summary>
    [DataField]
    public EmagType Flag = EmagType.Interaction;
}
