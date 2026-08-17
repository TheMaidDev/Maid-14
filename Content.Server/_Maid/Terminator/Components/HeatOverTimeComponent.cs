using Content.Server._Maid.Terminator.EntitySystems;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Server._Maid.Terminator.Components;

[RegisterComponent, Access(typeof(HeatOverTimeSystem))]
public sealed partial class HeatOverTimeComponent : Component
{
    [DataField(required: true)]
    public float Heat { get; set; }

    [DataField]
    public float FireStacks { get; set; }

    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan NextTickTime = TimeSpan.Zero;

    [DataField]
    public bool IgnoreHeatResistance { get; set; } = false;

    [DataField]
    public float MultiplierIncrease { get; set; }

    [DataField]
    public float Multiplier { get; set; } = 1f;

    [DataField]
    public float FireProtectionPenetration { get; set; }
}
