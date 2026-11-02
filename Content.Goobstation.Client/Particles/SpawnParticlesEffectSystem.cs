// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Particles;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing; // MAID particle lifecycle fix

namespace Content.Goobstation.Client.Particles;

public sealed class SpawnParticlesEffectSystem : SharedSpawnParticlesEffectSystem
{
    [Dependency] private readonly ParticleSystem _particles = default!;
    // MAID BEGIN particle lifecycle fix
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    // MAID END particle lifecycle fix

    protected override void SpawnParticles(ProtoId<ParticleEffectPrototype> particleProto, EntityUid target, Color? color, bool attached)
    {
        base.SpawnParticles(particleProto, target, color, attached);

        // MAID BEGIN particle lifecycle fix
        // Prediction re-runs would spawn duplicate emitters.
        if (!_timing.IsFirstTimePredicted)
            return;

        // A detached infinite emitter has nothing that would ever stop it. One-shot bursts exhaust themselves.
        if (!attached
            && _proto.Resolve(particleProto, out var proto)
            && proto.Duration <= TimeSpan.Zero
            && !proto.Burst)
        {
            Log.Warning($"Refusing to spawn infinite particle effect {particleProto} without attaching it to an entity.");
            return;
        }
        // MAID END particle lifecycle fix

        _particles.CreateParticle(particleProto, target, color, attached);
    }
}
