using Robust.Shared.Map;

namespace Content.Server._Maid.TradeShuttleConsole;

[RegisterComponent]
public sealed partial class AttachedTradeMapComponent : Component
{
    [DataField]
    public MapId AttachedMap = MapId.Nullspace;
}
