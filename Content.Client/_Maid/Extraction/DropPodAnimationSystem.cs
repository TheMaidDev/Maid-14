using System.Numerics;
using Content.Client.Storage.Visualizers;
using Content.Shared._Maid.Extraction;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Content.Shared.Storage;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client._Maid.Extraction;

/// <summary>
/// Plays the drop pod flying up and away, gaining speed. The real pod is already gone on the server,
/// so a local copy of its sprite is animated, like the fulton balloon.
/// </summary>
public sealed class DropPodAnimationSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _player = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private const string AnimationKey = "drop-pod-launch";
    private const string LandingKey = "drop-pod-landing";
    private const string SmokeKey = "drop-pod-smoke";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DropPodLaunchAnimationEvent>(OnLaunchAnimation);
        SubscribeLocalEvent<DropPodComponent, ComponentStartup>(OnPodStartup);
        SubscribeLocalEvent<DropPodComponent, AnimationCompletedEvent>(OnPodAnimationCompleted);
        // the storage visualizer shows the closed door on every appearance update, hide it again while landing
        SubscribeLocalEvent<DropPodComponent, AppearanceChangeEvent>(OnPodAppearanceChange,
            after: new[] { typeof(EntityStorageVisualizerSystem) });
    }

    private bool IsLanding(DropPodComponent pod)
    {
        return _timing.CurTime - pod.LandedAt <= pod.LandingAnimationLength;
    }

    private void SetDoorVisible(EntityUid uid, SpriteComponent sprite, bool visible)
    {
        if (_sprite.LayerMapTryGet((uid, sprite), StorageVisualLayers.Door, out var door, false))
            _sprite.LayerSetVisible((uid, sprite), door, visible);
    }

    private void OnPodAppearanceChange(Entity<DropPodComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite != null && IsLanding(ent.Comp))
            SetDoorVisible(ent.Owner, args.Sprite, false);
    }

    /// <summary>
    /// A pod that just hit the station plays its landing once. Pods seen later (e.g. coming into view) stay still.
    /// </summary>
    private void OnPodStartup(Entity<DropPodComponent> ent, ref ComponentStartup args)
    {
        var length = ent.Comp.LandingAnimationLength;
        if (!IsLanding(ent.Comp) || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // the door is drawn by the storage visualizer on top, hide it until the pod has risen
        SetDoorVisible(ent.Owner, sprite, false);

        var animation = new Animation
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = StorageVisualLayers.Base,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(ent.Comp.LandingState), 0f),
                        new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(ent.Comp.BaseState), (float) length.TotalSeconds),
                    },
                },
            },
        };

        _player.Play(ent.Owner, animation, LandingKey);
    }

    private void OnPodAnimationCompleted(Entity<DropPodComponent> ent, ref AnimationCompletedEvent args)
    {
        if (args.Key != LandingKey || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // back to what the storage visualizer would show: the door only while closed
        _appearance.TryGetData<bool>(ent.Owner, StorageVisuals.Open, out var open);
        SetDoorVisible(ent.Owner, sprite, !open);
    }

    private void OnLaunchAnimation(DropPodLaunchAnimationEvent ev)
    {
        var coordinates = GetCoordinates(ev.Coordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        if (ev.Smoke != null)
            _trails.Add(new SmokeTrail(coordinates, ev.Smoke, ev.Duration, ev.Offset));

        var animationEnt = Spawn(null, coordinates);
        var sprite = AddComp<SpriteComponent>(animationEnt);
        sprite.NoRotation = true;
        // above everything on the station and above the smoke
        _sprite.SetDrawDepth((animationEnt, sprite), (int) DrawDepth.Effects);

        foreach (var layer in ev.Layers)
        {
            _sprite.AddLayer((animationEnt, sprite), layer);
        }

        var despawn = AddComp<TimedDespawnComponent>(animationEnt);
        despawn.Lifetime = ev.Duration + 0.1f;

        _player.Play(animationEnt, CreateAnimation(ev.Duration, ev.Offset), AnimationKey);
    }

    /// <summary>
    /// How far below the pod sprite the smoke comes out, in tiles.
    /// </summary>
    private static readonly Vector2 SmokeExhaust = new(0f, -0.4f);

    /// <summary>
    /// Seconds between two smoke puffs while the pod rises.
    /// </summary>
    private const float PuffInterval = 0.08f;

    /// <summary>
    /// How long one puff lives, growing and thinning out where it was left.
    /// </summary>
    private const float PuffLife = 1.2f;

    private const float PuffStartScale = 0.35f;
    private const float PuffEndScale = 1f;

    /// <summary>
    /// Sideways scatter of the puffs, in tiles.
    /// </summary>
    private const float PuffJitter = 0.15f;

    /// <summary>
    /// A rising pod that still leaves smoke behind it.
    /// </summary>
    private sealed class SmokeTrail(EntityCoordinates coordinates, SpriteSpecifier smoke, float duration, Vector2 offset)
    {
        public readonly EntityCoordinates Coordinates = coordinates;
        public readonly SpriteSpecifier Smoke = smoke;
        public readonly float Duration = duration;
        public readonly Vector2 Offset = offset;
        public float Elapsed;
        public float NextPuff;
    }

    private readonly List<SmokeTrail> _trails = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        for (var i = _trails.Count - 1; i >= 0; i--)
        {
            var trail = _trails[i];
            trail.Elapsed += frameTime;

            while (trail.NextPuff <= Math.Min(trail.Elapsed, trail.Duration))
            {
                SpawnPuff(trail, trail.NextPuff);
                trail.NextPuff += PuffInterval;
            }

            if (trail.Elapsed >= trail.Duration)
                _trails.RemoveAt(i);
        }
    }

    /// <summary>
    /// One puff of exhaust left where the pod was at <paramref name="time"/>. It stays there while the pod
    /// flies on, so together the puffs make a trail. Drawn one depth below the pod so it stays behind it,
    /// and as thin as the pod is at that moment, so the trail fades out together with it.
    /// </summary>
    private void SpawnPuff(SmokeTrail trail, float time)
    {
        if (!trail.Coordinates.IsValid(EntityManager))
            return;

        var progress = time / trail.Duration;
        var alpha = progress <= FadeStart ? 1f : Math.Clamp((1f - progress) / (1f - FadeStart), 0f, 1f);
        if (alpha <= 0f)
            return;

        var offset = trail.Offset + SmokeExhaust
            + new Vector2(_random.NextFloat(-PuffJitter, PuffJitter), RiseAt(progress));

        var puff = Spawn(null, trail.Coordinates);
        var sprite = AddComp<SpriteComponent>(puff);
        sprite.NoRotation = true;
        _sprite.SetDrawDepth((puff, sprite), (int) DrawDepth.Overdoors);
        _sprite.AddLayer((puff, sprite), trail.Smoke);
        _sprite.SetOffset((puff, sprite), offset);
        _sprite.SetScale((puff, sprite), new Vector2(PuffStartScale));
        _sprite.SetColor((puff, sprite), Color.White.WithAlpha(alpha));

        var despawn = AddComp<TimedDespawnComponent>(puff);
        despawn.Lifetime = PuffLife + 0.1f;

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(PuffLife),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(alpha), 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), PuffLife),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(PuffStartScale), 0f),
                        new AnimationTrackProperty.KeyFrame(new Vector2(PuffEndScale), PuffLife),
                    },
                },
            },
        };

        _player.Play(puff, animation, SmokeKey);
    }

    /// <summary>
    /// How high the pod rises over the whole lift-off, in tiles.
    /// </summary>
    private const float LaunchHeight = 8f;

    /// <summary>
    /// Key frames the rise is sampled into; linear interpolation between them looks smooth.
    /// </summary>
    private const int LaunchSamples = 24;

    /// <summary>
    /// Part of the lift-off after which the sprite starts fading out, fully transparent at the end.
    /// </summary>
    private const float FadeStart = 0.5f;

    /// <summary>
    /// Part of the rise covered at a steady speed; the rest is constant acceleration.
    /// Without it the pod barely moves at first.
    /// </summary>
    private const float StartSpeedShare = 0.3f;

    /// <summary>
    /// Leaves with some speed right away, then keeps accelerating evenly
    /// (height = steady part + part growing with the square of time), and fades out
    /// over the second half. Key frame times are deltas from the previous frame.
    /// </summary>
    private static Animation CreateAnimation(float duration, Vector2 offset)
    {
        var rise = CreateRiseTrack(duration, offset);

        var fade = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Color),
            InterpolationMode = AnimationInterpolationMode.Linear,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(Color.White, 0f),
                new AnimationTrackProperty.KeyFrame(Color.White, duration * FadeStart),
                new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), duration * (1f - FadeStart)),
            },
        };

        return new Animation
        {
            Length = TimeSpan.FromSeconds(duration),
            AnimationTracks = { rise, fade },
        };
    }

    /// <summary>
    /// Sprite offset track of the rise.
    /// </summary>
    private static AnimationTrackComponentProperty CreateRiseTrack(float duration, Vector2 offset)
    {
        var rise = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Offset),
            InterpolationMode = AnimationInterpolationMode.Linear,
        };

        var step = duration / LaunchSamples;
        rise.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(offset, 0f));
        for (var i = 1; i <= LaunchSamples; i++)
        {
            var t = (float) i / LaunchSamples;
            rise.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(offset + new Vector2(0f, RiseAt(t)), step));
        }

        return rise;
    }

    /// <summary>
    /// Height of the pod at <paramref name="progress"/> (0 to 1) of the lift-off.
    /// </summary>
    private static float RiseAt(float progress)
    {
        return LaunchHeight * (StartSpeedShare * progress + (1f - StartSpeedShare) * progress * progress);
    }
}
