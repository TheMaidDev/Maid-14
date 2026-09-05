using Robust.Shared.Prototypes;

namespace Content.Shared._White.Antag;

[Prototype("antagonist")]
public sealed partial class AntagonistPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Loc id of the name displayed in the ghost teleport menu.
    /// </summary>
    [DataField(required: true)]
    public LocId Name = default!;

    /// <summary>
    ///     Loc id of the description shown as the button tooltip.
    /// </summary>
    [DataField(required: true)]
    public LocId Description = default!;

    /// <summary>
    ///     Groups with a lower weight are listed first. Player antagonists always come before these.
    /// </summary>
    [DataField(required: true)]
    public int Weight;
}
