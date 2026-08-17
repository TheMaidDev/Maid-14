using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Conditions;

public sealed partial class AdaptiveScorePrototypeCondition : IAdaptiveScoreCondition
{
    [DataField(required: true)]
    public List<string> Prototypes { get; set; } = new();

    public bool ConditionMet(EntityUid owner, EntityUid? mob, Entity<MindComponent>? mind,  IEntityManager entMan)
    {
        if (mob is null)
            return false;

        if (!entMan.TryGetComponent<MetaDataComponent>(mob.Value, out var meta) || meta.EntityPrototype == null)
            return false;

        return Prototypes.Contains(meta.EntityPrototype.ID);
    }
}
