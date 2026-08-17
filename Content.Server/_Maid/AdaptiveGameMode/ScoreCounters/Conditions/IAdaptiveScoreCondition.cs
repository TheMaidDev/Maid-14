using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

[ImplicitDataDefinitionForInheritors]
public partial interface IAdaptiveScoreCondition
{
    /*public struct Result
    {
        public bool Passes;
        public float ChaosMultiplier;
        public float CombatMultiplier;

        public static implicit operator Result(bool passes) =>
            passes ? Pass() : No;

        public static Result No { get; } = new()
        {
            Passes = false,
            ChaosMultiplier = 0f,
            CombatMultiplier = 0f,
        };

        public static Result Pass(float multiplier = 1f) =>
            Pass(1, 1);

        public static Result Pass(float chaos, float combat) => new()
        {
            Passes = true,
            ChaosMultiplier = chaos,
            CombatMultiplier = combat,
        };

        public static Result Pass(float shared, float chaos, float combat) =>
            Pass(shared * chaos, shared * combat);
    }*/

    public bool ConditionMet(EntityUid owner, EntityUid? controlledMob, Entity<MindComponent>? mind, IEntityManager entMan);

    public static EntityUid? ResolveTarget(
        AdaptiveScoreConditionTarget target,
        EntityUid owner,
        EntityUid? controlledMob,
        Entity<MindComponent>? mind
    ) => target switch
    {
        AdaptiveScoreConditionTarget.Owner => owner,
        AdaptiveScoreConditionTarget.Mind => mind,
        AdaptiveScoreConditionTarget.Mob => controlledMob,
        _ => null,
    };
}

[Serializable]
public enum AdaptiveScoreConditionTarget
{
    Owner = 0,
    Mind = 1,
    Mob = 2, // Controlled mob by mind
}
