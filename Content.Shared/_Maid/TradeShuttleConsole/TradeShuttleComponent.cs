using Content.Shared.Timing;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Maid.TradeShuttleConsole;

/// <summary>
/// Added to a shuttle grid when it is being controlled by a Trade Shuttle Console.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TradeShuttleComponent : Component
{
    [ViewVariables]
    public MapId TradeMap;

    [ViewVariables]
    public EntityUid? Console;
}
