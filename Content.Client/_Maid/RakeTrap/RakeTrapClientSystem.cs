using System.Numerics;
using Content.Client.Interactable.Components;
using Content.Shared._Maid.RakeTrap;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Maid.RakeTrap;

/// <summary>
///     Client-side rake visuals: sprite state follows the replicated trap state, and when the
///     rake fires the sprite plays a cock-then-whack animation (tilts up, snaps down).
/// </summary>
public sealed class RakeTrapClientSystem : SharedRakeTrapSystem
{
    private const string WhackKey = "rake_whack";

    [Dependency] private readonly AnimationPlayerSystem _animation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RakeTrapComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<RakeTrapComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(ent);

        if (ent.Comp.HitAt != null && !_animation.HasRunningAnimation(ent.Owner, WhackKey))
            PlayWhack(ent);
    }

    private void UpdateSprite(Entity<RakeTrapComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var state = ent.Comp.HitAt != null
            ? "rake_hit"
            : ent.Comp.Armed
                ? "rake_armed"
                : "rake";
        sprite.LayerSetState(0, state);

        // The deployed trap must not glow on hover: drop the interaction outline while armed
        // (StealthSystem pattern), restore it once the rake is a normal item again.
        if (ent.Comp.Armed)
            RemCompDeferred<InteractionOutlineComponent>(ent);
        else
            EnsureComp<InteractionOutlineComponent>(ent);
    }

    private void PlayWhack(Entity<RakeTrapComponent> ent)
    {
        var animComp = EnsureComp<AnimationPlayerComponent>(ent);
        _animation.Stop((ent, animComp), WhackKey);

        // Instant-feel whack: a quick snap up and back down (timings are presentation-only).
        const float riseTime = 0.08f;
        const float smackTime = 0.22f;
        const float settleTime = 0.45f;
        const float total = settleTime;

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(total),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Angle.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(-20f), riseTime),
                        new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(15f), smackTime),
                        new AnimationTrackProperty.KeyFrame(Angle.Zero, total),
                    },
                },
                // Fly up as it smacks, then fall back to the floor.
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0f, 0.25f), smackTime),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, total),
                    },
                },
            },
        };

        _animation.Play((ent, animComp), animation, WhackKey);
    }
}
