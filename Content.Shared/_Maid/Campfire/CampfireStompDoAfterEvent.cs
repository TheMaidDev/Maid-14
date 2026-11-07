using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Campfire;

[Serializable, NetSerializable]
public sealed partial class CampfireStompDoAfterEvent : SimpleDoAfterEvent;
