using Robust.Shared.GameObjects;

namespace Content.Server.Bed.Cryostorage.Events;

[ByRefEvent]
public record struct CryostorageBodyRemovedEvent(EntityUid Body, EntityUid? Station);