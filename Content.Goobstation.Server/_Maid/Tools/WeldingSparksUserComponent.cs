using Content.Goobstation.Server.Tools;

namespace Content.Goobstation.Server._Maid.Tools;

/// <summary>
/// Added to an entity while it welds with a tool that has a <see cref="Shared.Tools.WeldingSparksComponent"/>.
/// DoAfter end events are raised on the tool, so nothing fires if the user is deleted mid-weld;
/// this lets <see cref="WeldingSparksSystem"/> delete the user's sparks on shutdown instead.
/// </summary>
[RegisterComponent, Access(typeof(WeldingSparksSystem))]
public sealed partial class WeldingSparksUserComponent : Component
{
    /// <summary>
    /// Tools whose <see cref="Shared.Tools.WeldingSparksComponent.SpawnedEffects"/> hold sparks of this user.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<EntityUid> Tools = [];
}
