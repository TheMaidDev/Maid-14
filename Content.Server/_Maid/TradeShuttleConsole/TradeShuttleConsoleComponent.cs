namespace Content.Server._Maid.TradeShuttleConsole;

[RegisterComponent]
public sealed partial class TradeShuttleConsoleComponent : Component
{
    [DataField]
    public EntityUid? LinkedToConsole = null;

    [DataField]
    public HashSet<EntityUid> LinkedFromConsole = [];
}
