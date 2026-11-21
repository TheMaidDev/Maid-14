using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Structures;

/// <summary>
/// Takes no damage and lets projectiles through while an anchored window stands on the same tile.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProtectedByWindowComponent : Component
{
    [DataField]
    public EntityWhitelist Whitelist = new()
    {
        Tags = new() { "Window" },
    };
}
