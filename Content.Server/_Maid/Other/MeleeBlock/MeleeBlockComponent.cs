namespace Content.Server._Maid.Other.MeleeBlock;

[RegisterComponent]
public sealed partial class MeleeBlockComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float BlockChance = 0.4f;
}
