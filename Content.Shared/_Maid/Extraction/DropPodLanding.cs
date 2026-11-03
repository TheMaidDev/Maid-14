using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// How a drop pod lands: what spawns, what happens to the tile, how loud and how hard it hits.
/// </summary>
[DataDefinition]
public sealed partial class DropPodLanding
{
    [DataField]
    public EntProtoId PodPrototype = "ExtractionDropPod";

    /// <summary>
    /// Gib whatever is alive and standing on the landing tile. Objects are left alone.
    /// </summary>
    [DataField]
    public bool CrushLiving = true;

    /// <summary>
    /// Impact heard nearby, positional. Same split as explosions and meteors.
    /// </summary>
    [DataField]
    public SoundSpecifier ImpactSound = new SoundCollectionSpecifier("Explosion");

    /// <summary>
    /// Impact heard by everyone else on the map.
    /// </summary>
    [DataField]
    public SoundSpecifier ImpactSoundFar = new SoundCollectionSpecifier("ExplosionFar", AudioParams.Default.WithVolume(2f));

    [DataField]
    public float ImpactNearRange = 20f;

    /// <summary>
    /// Screens shake within this range of the landing, stronger closer to it.
    /// </summary>
    [DataField]
    public float ShakeRange = 10f;

    /// <summary>
    /// Camera kick at the landing point, fading out to the edge of <see cref="ShakeRange"/>.
    /// </summary>
    [DataField]
    public float ShakeStrength = 15f;
}
