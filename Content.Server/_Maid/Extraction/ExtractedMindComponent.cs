namespace Content.Server._Maid.Extraction;

/// <summary>
/// Marks a mind whose character left the round through extraction, so they count as survived
/// unless they were already dead when the pod left, and can't come back through ghost roles.
/// </summary>
[RegisterComponent]
public sealed partial class ExtractedMindComponent : Component
{
    /// <summary>
    /// The character was dead when extracted (a dead body pushed into the pod still leaves).
    /// </summary>
    [ViewVariables]
    public bool Dead;
}
