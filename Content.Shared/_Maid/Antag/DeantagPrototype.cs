using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Antag;

/// <summary>
///     Describes how to strip a single kind of antagonist from a player.
///     The prototype id must match the <c>AntagPrototype</c> id of the mind roles it cleans up
///     (e.g. <c>Traitor</c>); the mind roles with that antag prototype are removed together with the listed components.
/// </summary>
[Prototype("deantag")]
public sealed partial class DeantagPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Components removed from the mind's current body.
    /// </summary>
    [DataField]
    public ComponentRegistry RemoveComponents { get; private set; } = new();

    /// <summary>
    ///     Components removed from the mind entity itself.
    /// </summary>
    [DataField]
    public ComponentRegistry RemoveMindComponents { get; private set; } = new();
}
