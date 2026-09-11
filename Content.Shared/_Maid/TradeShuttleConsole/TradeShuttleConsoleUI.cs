using Robust.Shared.Serialization;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;

namespace Content.Shared._Maid.TradeShuttleConsole;

[Serializable, NetSerializable]
public enum TradeShuttleConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable, Virtual]
public class TradeShuttleConsoleUIState : BoundUserInterfaceState
{
    public required string? ControlledShuttle;
    public required bool OnTrade; // When ftl means target
}

[Serializable, NetSerializable]
public sealed class TradeShuttleConsoleFtlInProgressUIState : TradeShuttleConsoleUIState
{
    public required StartEndTime FtlTime;
    public required FTLState FtlState;
}

[Serializable, NetSerializable]
public sealed class TradeShuttleConsoleIdleUIState : TradeShuttleConsoleUIState
{

}


[Serializable, NetSerializable]
public sealed class TradeShuttleConsoleErrorUIState : TradeShuttleConsoleUIState
{
    public enum ErrorType
    {
        ShuttleNotFound
    }

    public required ErrorType Error;
}

[Serializable, NetSerializable]
public sealed class TradeShuttleConsoleButtonPressedMessage : BoundUserInterfaceMessage;
