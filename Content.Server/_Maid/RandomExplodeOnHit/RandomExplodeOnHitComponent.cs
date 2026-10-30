using Robust.Shared.Prototypes;

namespace Content.Server._Maid.RandomExplodeOnHit;

[RegisterComponent]
[AutoGenerateComponentPause]
public sealed partial class RandomExplodeOnHitComponent : Component
{
    [DataField]
    public TimeSpan TimeBetweenHits { get; set; } = TimeSpan.FromSeconds(30);

    [DataField]
    public int CriticalHitCount { get; set; } = 2;

    [DataField]
    public float ExplodeChance { get; set; } = 0.25f;

    [DataField]
    [AutoPausedField]
    public TimeSpan? LastHit { get; set; }

    [DataField]
    public int HitCount { get; set; }

    [DataField(required: true)]
    public EntProtoId BrokenPrototype { get; set; }

    [DataField]
    public string Solution { get; set; } = "spray";

    [DataField]
    public float MinFill { get; set; } = 0.25f;

    [DataField]
    public float MaxTotalIntensity { get; set; } = 40f;


}
