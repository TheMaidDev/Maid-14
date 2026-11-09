using Content.Shared._Maid.Salvage;
using Content.Shared.Procedural;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Salvage;

/// <summary>
/// Mission objectives, duration modifiers and rewards of salvage expeditions,
/// returned from the pre-rework expeditions.
/// </summary>
public abstract partial class SharedSalvageSystem
{
    /// <summary>
    /// Faction config with the structure to destroy on <see cref="SalvageMissionType.Destruction"/>.
    /// </summary>
    public const string DefenseStructureConfig = "DefenseStructure";

    /// <summary>
    /// Faction config with the creature to kill on <see cref="SalvageMissionType.Elimination"/>.
    /// </summary>
    public const string MegafaunaConfig = "Megafauna";

    /// <summary>
    /// Faction config key the mission type needs.
    /// </summary>
    public static string GetObjectiveConfig(SalvageMissionType type)
    {
        return type switch
        {
            SalvageMissionType.Destruction => DefenseStructureConfig,
            SalvageMissionType.Elimination => MegafaunaConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    /// <summary>
    /// Gets the entity prototype the mission objective is about, if the faction has it.
    /// </summary>
    public bool TryGetObjectivePrototype(SalvageMissionType type, string faction, out EntProtoId proto)
    {
        proto = default;

        if (!_proto.TryIndex<SalvageFactionPrototype>(faction, out var factionProto) ||
            !factionProto.Configs.TryGetValue(GetObjectiveConfig(type), out var id) ||
            !_proto.HasIndex<EntityPrototype>(id))
        {
            return false;
        }

        proto = id;
        return true;
    }

    /// <summary>
    /// Player-friendly description of the mission objective.
    /// </summary>
    public string GetMissionDescription(SalvageMissionType type, SalvageDifficultyPrototype difficulty, string faction)
    {
        if (!TryGetObjectivePrototype(type, faction, out var target))
            return string.Empty;

        var name = _proto.Index(target).Name;

        return type switch
        {
            SalvageMissionType.Destruction => Loc.GetString("salvage-expedition-desc-structure",
                ("count", difficulty.StructureCount),
                ("structure", name)),
            SalvageMissionType.Elimination => Loc.GetString("salvage-expedition-desc-elimination",
                ("creature", name)),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Reward name with the prototype suffix, so different bottles or cash stacks can be told apart.
    /// </summary>
    public string GetRewardName(string reward)
    {
        if (!_proto.TryIndex<EntityPrototype>(reward, out var proto))
            return reward;

        return string.IsNullOrEmpty(proto.EditorSuffix)
            ? proto.Name
            : $"{proto.Name} ({proto.EditorSuffix})";
    }

    /// <summary>
    /// Rolls the duration modifier and returns the mission duration.
    /// The modifier description is added to <paramref name="mods"/>.
    /// </summary>
    private TimeSpan GetMissionDuration(System.Random rand, ref float budget, List<string> mods)
    {
        var time = GetMod<SalvageTimeMod>(rand, ref budget);

        // Round the duration to the nearest 15 seconds.
        var exactDuration = MathHelper.Lerp(time.MinDuration, time.MaxDuration, rand.NextFloat());
        exactDuration = MathF.Round(exactDuration / 15f) * 15f;

        if (time.Description is { } desc)
            mods.Add(Loc.GetString(desc));

        return TimeSpan.FromSeconds(exactDuration);
    }

    /// <summary>
    /// Rolls every reward table of the difficulty once.
    /// </summary>
    private List<string> GetMissionRewards(SalvageDifficultyPrototype difficulty, System.Random rand)
    {
        var rewards = new List<string>(difficulty.Rewards.Count);

        foreach (var id in difficulty.Rewards)
        {
            rewards.Add(_proto.Index(id).Pick(rand).Id);
        }

        return rewards;
    }
}
