using Content.Shared._Maid.Tools;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Tools;

public sealed class WeldingGlowParticleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    /// <summary>
    /// Lowest flicker value as a fraction of <see cref="WeldingGlowParticleComponent.Energy"/>.
    /// </summary>
    private const float MinEnergyFraction = 0.25f;

    public override void FrameUpdate(float frameTime)
    {
        var curTime = _timing.CurTime;
        var time = (float) _timing.RealTime.TotalSeconds;

        var query = EntityQueryEnumerator<WeldingGlowParticleComponent, PointLightComponent>();
        while (query.MoveNext(out var uid, out var glow, out var light))
        {
            var blend = GetBlend(glow, curTime);
            var smooth = blend * blend * (3f - 2f * blend);
            var noise = FlickerNoise(time, uid.Id * 0.731f) * 0.5f + 0.5f;
            var flicker = glow.Energy * (MinEnergyFraction + (1f - MinEnergyFraction) * noise);

            _lights.SetRadius(uid, glow.Radius, light);
            _lights.SetEnergy(uid, flicker * smooth, light);
        }
    }

    private static float GetBlend(WeldingGlowParticleComponent glow, TimeSpan curTime)
    {
        if (glow.FadeTime <= 0f)
            return glow.EndTime == null ? 1f : 0f;

        var fadeIn = Math.Clamp((float) (curTime - glow.StartTime).TotalSeconds / glow.FadeTime, 0f, 1f);
        if (glow.EndTime is not { } end)
            return fadeIn;

        // Started fading out before fully faded in: continue from where the fade in got to.
        var fadeInAtEnd = Math.Clamp((float) (end - glow.StartTime).TotalSeconds / glow.FadeTime, 0f, 1f);
        var fadeOut = Math.Clamp(1f - (float) (curTime - end).TotalSeconds / glow.FadeTime, 0f, 1f);
        return fadeInAtEnd * fadeOut;
    }

    /// <summary>
    /// Several stacked sines, result in range [-1, 1].
    /// </summary>
    private static float FlickerNoise(float time, float offset)
    {
        var value = MathF.Sin(time * 11f + offset)
                    + MathF.Sin(time * 23.7f + offset * 1.7f) * 0.7f
                    + MathF.Sin(time * 41.3f + offset * 0.6f) * 0.5f
                    + MathF.Sin(time * 5.1f + offset * 2.3f) * 0.8f;
        return value / 3f;
    }
}
