using Content.Shared._Maid.Tools;
using Content.Shared.DoAfter;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Tools;

public sealed class WeldingGlowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    /// <summary>
    /// Lowest flicker value as a fraction of <see cref="WeldingGlowComponent.Energy"/>.
    /// </summary>
    private const float MinEnergyFraction = 0.25f;

    private EntityQuery<DoAfterComponent> _doAfterQuery;

    public override void Initialize()
    {
        base.Initialize();

        _doAfterQuery = GetEntityQuery<DoAfterComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        var time = (float) _timing.RealTime.TotalSeconds;

        var query = EntityQueryEnumerator<WeldingGlowComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var glow, out var light, out var xform))
        {
            var welding = TryComp<WelderComponent>(uid, out var welder)
                          && welder.Enabled
                          && IsWelding(uid, xform.ParentUid);

            if (!glow.Active)
            {
                if (!welding)
                    continue;

                glow.Active = true;
                glow.OriginalColor = light.Color;
                glow.OriginalEnergy = light.Energy;
                glow.OriginalRadius = light.Radius;
            }

            var step = glow.FadeTime > 0f ? frameTime / glow.FadeTime : 1f;
            glow.Blend = welding
                ? MathF.Min(1f, glow.Blend + step)
                : MathF.Max(0f, glow.Blend - step);

            if (glow.Blend <= 0f)
            {
                glow.Active = false;
                _lights.SetColor(uid, glow.OriginalColor, light);
                _lights.SetEnergy(uid, glow.OriginalEnergy, light);
                _lights.SetRadius(uid, glow.OriginalRadius, light);
                continue;
            }

            var blend = glow.Blend * glow.Blend * (3f - 2f * glow.Blend);
            var noise = FlickerNoise(time, uid.Id * 0.731f) * 0.5f + 0.5f;
            var flicker = glow.Energy * (MinEnergyFraction + (1f - MinEnergyFraction) * noise);

            _lights.SetColor(uid, Color.InterpolateBetween(glow.OriginalColor, Color.White, blend), light);
            _lights.SetEnergy(uid, MathHelper.Lerp(glow.OriginalEnergy, flicker, blend), light);
            _lights.SetRadius(uid, MathHelper.Lerp(glow.OriginalRadius, glow.Radius, blend), light);
        }
    }

    private bool IsWelding(EntityUid welder, EntityUid holder)
    {
        if (!_doAfterQuery.TryGetComponent(holder, out var doAfters))
            return false;

        foreach (var doAfter in doAfters.DoAfters.Values)
        {
            if (doAfter.Completed || doAfter.Cancelled)
                continue;

            if (doAfter.Args.Used == welder && doAfter.Args.Event is SharedToolSystem.ToolDoAfterEvent)
                return true;
        }

        return false;
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
