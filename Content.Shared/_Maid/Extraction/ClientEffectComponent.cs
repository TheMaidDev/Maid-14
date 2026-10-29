using Robust.Shared.Prototypes;

namespace Content.Shared._Maid.Extraction;

/// <summary>
/// Keeps a client-only entity attached to this one for as long as it exists on the client.
/// Needed for client effects that start on MapInit (like the Goob particle emitter): the client never
/// map-inits entities that come from the server, only the ones it spawns itself.
/// </summary>
[RegisterComponent]
public sealed partial class ClientEffectComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [ViewVariables]
    public EntityUid? Spawned;
}
