using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Maid.DocumentPrinter;

/// <summary>
/// Can work with <see cref="Content.Shared.Cargo.Components.CargoOrderConsoleComponent"/> in
/// <see cref="Content.Shared.Cargo.Components.CargoOrderConsoleMode.PrintSlip"/> mode on the same entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class DocumentPrinterComponent : Component
{
    [DataField]
    public List<DocumentSection> Sections = new();

    [DataField]
    public List<DocumentSection> EmagSections = new();

    [DataField]
    public SoundSpecifier PrintSound = new SoundCollectionSpecifier("PrinterPrint");

    [DataField]
    public TimeSpan PrintDelay = TimeSpan.FromSeconds(3.6);

    /// <summary>
    /// Document waiting for the print animation to finish.
    /// </summary>
    public EntProtoId? PendingDocument;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPrintTime;

    [ViewVariables]
    public bool Printing;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class DocumentSection
{
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public List<EntProtoId> Documents = new();
}
