using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.DocumentPrinter;

[Serializable, NetSerializable]
public enum DocumentPrinterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum DocumentPrinterVisuals : byte
{
    Printing,
}

[Serializable, NetSerializable]
public enum DocumentPrinterVisualLayers : byte
{
    Base,
}

[Serializable, NetSerializable]
public sealed class DocumentPrinterBuiState : BoundUserInterfaceState
{
    public required List<DocumentSection> Sections { get; init; }
    public required List<ProtoId<CargoProductPrototype>> Products { get; init; }
}

[Serializable, NetSerializable]
public sealed class DocumentPrinterPrintMessage(EntProtoId document) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Document = document;
}
