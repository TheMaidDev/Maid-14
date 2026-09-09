namespace Content.Server._Maid.TradeShuttleConsole;

[RegisterComponent]
public sealed partial class TradeMapComponent : Component
{
    [DataField]
    public EntityUid AttachedStation;
}
