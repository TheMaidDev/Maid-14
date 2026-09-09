namespace Content.Server._Maid.TradeShuttleConsole;

[RegisterComponent]
public sealed partial class TradeShuttleConsoleComponent : Component
{
    [DataField]
    public float StartupTime = 5.0f;

    [DataField]
    public float TravelTime = 20.0f;

    [DataField]
    public EntityUid? LinkedToConsole = null;

    [DataField]
    public HashSet<EntityUid> LinkedFromConsole = [];
}
