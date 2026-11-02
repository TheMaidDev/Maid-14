using Content.Shared.DoAfter;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Tools;

/// <summary>
/// Makes a welder glow bright white and flicker (stacked sines) while it is actively welding something.
/// Brightness smoothly blends to and from the flicker value.
/// </summary>
public sealed class WeldingGlowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    /// <summary>
    /// Seconds to fully fade in or out.
    /// </summary>
    private const float FadeTime = 0.7f;

    private const float MinEnergy = 1f;
    private const float MaxEnergy = 4f;

    private sealed class GlowState
    {
        public float Blend;
        public Color OriginalColor;
        public float OriginalEnergy;
    }

    private readonly Dictionary<EntityUid, GlowState> _states = new();
    private readonly List<EntityUid> _toRemove = new();

    private EntityQuery<DoAfterComponent> _doAfterQuery;
    private EntityQuery<PointLightComponent> _lightQuery;

    public override void Initialize()
    {
        base.Initialize();

        _doAfterQuery = GetEntityQuery<DoAfterComponent>();
        _lightQuery = GetEntityQuery<PointLightComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        var welding = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WelderComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var welder, out _, out var xform))
        {
            if (!welder.Enabled || !IsWelding(uid, xform.ParentUid))
                continue;

            welding.Add(uid);

            if (_states.ContainsKey(uid))
                continue;

            var light = _lightQuery.GetComponent(uid);
            _states[uid] = new GlowState
            {
                OriginalColor = light.Color,
                OriginalEnergy = light.Energy,
            };
        }

        var time = (float) _timing.RealTime.TotalSeconds;
        var step = frameTime / FadeTime;

        _toRemove.Clear();
        foreach (var (uid, state) in _states)
        {
            if (TerminatingOrDeleted(uid) || !_lightQuery.TryGetComponent(uid, out var light))
            {
                _toRemove.Add(uid);
                continue;
            }

            state.Blend = welding.Contains(uid)
                ? MathF.Min(1f, state.Blend + step)
                : MathF.Max(0f, state.Blend - step);

            if (state.Blend <= 0f)
            {
                _lights.SetColor(uid, state.OriginalColor, light);
                _lights.SetEnergy(uid, state.OriginalEnergy, light);
                _toRemove.Add(uid);
                continue;
            }

            var blend = state.Blend * state.Blend * (3f - 2f * state.Blend);
            var noise = FlickerNoise(time, uid.Id * 0.731f);
            var flicker = MinEnergy + (MaxEnergy - MinEnergy) * (noise * 0.5f + 0.5f);

            _lights.SetColor(uid, Color.InterpolateBetween(state.OriginalColor, Color.White, blend), light);
            _lights.SetEnergy(uid, MathHelper.Lerp(state.OriginalEnergy, flicker, blend), light);
        }

        foreach (var uid in _toRemove)
        {
            _states.Remove(uid);
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
