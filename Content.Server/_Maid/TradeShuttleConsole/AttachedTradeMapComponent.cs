using Content.Shared.Cargo;
using Robust.Shared.Map;

namespace Content.Server._Maid.TradeShuttleConsole;

[RegisterComponent]
public sealed partial class AttachedTradeMapComponent : Component
{
    [ViewVariables]
    public MapId AttachedMap = MapId.Nullspace;

    [ViewVariables]
    public List<CargoOrderData> ApprovedOrders = [];
}
