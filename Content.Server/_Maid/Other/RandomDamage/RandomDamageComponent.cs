namespace Content.Server._Maid.Other.RandomDamage;

[RegisterComponent]
public sealed partial class RandomDamageComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Max = 50f;
}
