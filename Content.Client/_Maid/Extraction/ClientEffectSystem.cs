using Content.Shared._Maid.Extraction;
using Robust.Shared.Map;

namespace Content.Client._Maid.Extraction;

/// <summary>
/// Spawns the client-only effect of a <see cref="ClientEffectComponent"/> and removes it with its owner.
/// Spawning is done on the next frame, not while the server state that created the owner is applied.
/// </summary>
public sealed class ClientEffectSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClientEffectComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<ClientEffectComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.Spawned);
        ent.Comp.Spawned = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ClientEffectComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Spawned is { } spawned && !TerminatingOrDeleted(spawned))
                continue;

            comp.Spawned = SpawnAttachedTo(comp.Effect, new EntityCoordinates(uid, 0, 0));
        }
    }
}
