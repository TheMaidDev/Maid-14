using System.Numerics;
using Content.Client.Storage.Visualizers;
using Content.Shared._Maid.Extraction;
using Content.Shared.Storage;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Timing;
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

    private const string AnimationKey = "drop-pod-launch";
    private const string LandingKey = "drop-pod-landing";

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

        var animationEnt = Spawn(null, coordinates);
        var sprite = AddComp<SpriteComponent>(animationEnt);
        sprite.NoRotation = true;

        foreach (var layer in ev.Layers)
        {
            _sprite.AddLayer((animationEnt, sprite), layer);
        }

        var despawn = AddComp<TimedDespawnComponent>(animationEnt);
        despawn.Lifetime = ev.Duration + 0.1f;

        _player.Play(animationEnt, CreateAnimation(ev.Duration, ev.Offset), AnimationKey);
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
    /// Rises with constant acceleration (height grows with the square of time) and fades out
    /// over the second half. Key frame times are deltas from the previous frame.
    /// </summary>
    private static Animation CreateAnimation(float duration, Vector2 offset)
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
            rise.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(offset + new Vector2(0f, LaunchHeight * t * t), step));
        }

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
}
