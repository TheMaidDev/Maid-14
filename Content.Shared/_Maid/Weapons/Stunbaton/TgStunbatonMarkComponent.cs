using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Maid.Weapons.Stunbaton;

/// <summary>
/// Remembers who recently stunned this mob, so a second baton from the same user cannot chain stuns.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TgStunbatonMarkComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<TgStunbatonMark> Marks = new();
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class TgStunbatonMark
{
    [DataField]
    public NetEntity User;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan Expires;
}
