using Robust.Shared.Map;

namespace Content.Server._Maid.TradeShuttleConsole;

[ByRefEvent]
public record struct BeforeFTLStartedEvent
{
    public bool Cancelled;
    public EntityUid Target;
}
