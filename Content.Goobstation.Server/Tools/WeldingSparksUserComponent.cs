// MAID welding sparks cleanup - whole file

using Content.Shared.DoAfter;

namespace Content.Goobstation.Server.Tools;

/// <summary>
/// Added to an entity while it is welding with a tool that has a <see cref="Shared.Tools.WeldingSparksComponent"/>.
/// Tracks the sparks it caused, so they get deleted if the user is deleted mid-weld and no DoAfter event fires.
/// </summary>
[RegisterComponent, Access(typeof(WeldingSparksSystem))]
public sealed partial class WeldingSparksUserComponent : Component
{
    /// <summary>
    /// Spawned sparks effect -> the tool that spawned it and the DoAfter it belongs to.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, (EntityUid Tool, DoAfterId DoAfter)> Effects = new();
}
