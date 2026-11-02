using Robust.Shared.Prototypes;

namespace Content.Server._Maid.PassiveEntitySpawner;

[RegisterComponent]
public sealed partial class MaidPassiveEntitySpawnerComponent : Component
{
    /// <summary>
    /// Base cooldown before scaling.
    /// An entity will spawn in this amount of time if no other entities are alive.
    /// </summary>
    [DataField]
    public TimeSpan BaseSpawnCooldown = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Target number of entities to spawn in <see cref="TotalSecondsAmount"/> per player,
    /// assuming none of the tracked entities die during that period.
    /// </summary>
    [DataField]
    public float TotalEntityAmountPerPlayer = 1f;

    /// <summary>
    /// Time period in which <see cref="TotalEntityAmountPerPlayer"/> per player will be spawned if none die.
    /// </summary>
    [DataField]
    public TimeSpan TotalSecondsAmount = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Maximum cooldown cap to prevent overflow or absurdly long intervals.
    /// </summary>
    [DataField("maxSpawnCooldown")]
    public TimeSpan MaxSpawnCooldown = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Cached player count to avoid recalculating the multiplier every frame.
    /// </summary>
    [DataField]
    public int? CachedPlayerCount;

    /// <summary>
    /// Multiplier calculated based on player count, totalEntityAmount, totalSecondsAmount, and baseSpawnCooldown.
    /// </summary>
    [DataField]
    public float SpawnMultiplier = 1f;

    /// <summary>
    /// Timestamp when the last entity was spawned.
    /// </summary>
    [DataField]
    public TimeSpan? LastSpawnedAt;

    /// <summary>
    /// Timestamp when the next entity is scheduled to spawn.
    /// </summary>
    [DataField]
    public TimeSpan? NextSpawnAt;

    /// <summary>
    /// Prototype ID for normal spawns.
    /// </summary>
    [DataField("spawn")]
    public EntProtoId? SpawnPrototype = "MobCarpDragon";

    /// <summary>
    /// Chance to spawn the strong prototype instead.
    /// </summary>
    [DataField]
    public float StrongSpawnChance = 0f;

    /// <summary>
    /// Prototype ID for strong spawns.
    /// </summary>
    [DataField("spawnStrong")]
    public EntProtoId? SpawnPrototypeStrong = "MobSharkDragon";
}
