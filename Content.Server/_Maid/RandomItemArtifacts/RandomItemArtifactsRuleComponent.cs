namespace Content.Server._Maid.RandomItemArtifacts;

[RegisterComponent, Access(typeof(RandomItemArtifactsSystem))]
public sealed partial class RandomItemArtifactsRuleComponent : Component
{
    /// <summary>
    ///     Percentage of items on the map that will become artifacts.
    /// </summary>
    [DataField]
    public float ConversionChance = 0.004f;
}
