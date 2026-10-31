// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Particles;

namespace Content.Goobstation.Client.Particles;

// MAID BEGIN particle lifecycle fix
/*
/// <summary>
/// Spawns a particle effect on this client when an entity with
/// <see cref="ParticleEmitterComponent"/> enters the local view (MapInitEvent).
/// </summary>
*/
/// <summary>
/// Spawns a particle effect on this client when an entity with
/// <see cref="ParticleEmitterComponent"/> starts up, and stops it when the component shuts down.
/// </summary>
// MAID END particle lifecycle fix
public sealed class ParticleEmitterSystem : EntitySystem
{
    [Dependency] private readonly ParticleSystem _particles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // MAID BEGIN particle lifecycle fix
    // MapInitEvent is never raised on the client for server-spawned entities, and the emitter was never stopped.
    private readonly Dictionary<EntityUid, ActiveEmitter> _emitters = new();
    // MAID END particle lifecycle fix

    public override void Initialize()
    {
        base.Initialize();

        // MAID BEGIN particle lifecycle fix
        /*
        SubscribeLocalEvent<ParticleEmitterComponent, MapInitEvent>(OnMapInit);
        */
        SubscribeLocalEvent<ParticleEmitterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ParticleEmitterComponent, ComponentShutdown>(OnShutdown);
        // MAID END particle lifecycle fix
    }

    // MAID BEGIN particle lifecycle fix
    /*
    private void OnMapInit(Entity<ParticleEmitterComponent> ent, ref MapInitEvent args)
    */
    private void OnStartup(Entity<ParticleEmitterComponent> ent, ref ComponentStartup args)
    // MAID END particle lifecycle fix
    {
        var coords = _transform.GetMapCoordinates(ent.Owner);
        var emitter = _particles.SpawnEffect(ent.Comp.Effect, coords, ent.Owner, ent.Comp.ColorOverride);
        if (emitter == null)
            return;

        if (ent.Comp.Intensity != 1f)
            emitter.Intensity = ent.Comp.Intensity;

        _emitters[ent.Owner] = emitter; // MAID particle lifecycle fix
    }

    // MAID BEGIN particle lifecycle fix
    private void OnShutdown(Entity<ParticleEmitterComponent> ent, ref ComponentShutdown args)
    {
        if (_emitters.Remove(ent.Owner, out var emitter))
            _particles.RemoveParticle(emitter);
    }
    // MAID END particle lifecycle fix
}
