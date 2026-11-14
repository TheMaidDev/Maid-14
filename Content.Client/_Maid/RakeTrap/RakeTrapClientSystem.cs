using System.Numerics;
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
    }

    private void PlayWhack(Entity<RakeTrapComponent> ent)
    {
        var animComp = EnsureComp<AnimationPlayerComponent>(ent);
        _animation.Stop((ent, animComp), WhackKey);

        var cockTime = (float)ent.Comp.CockTime.TotalSeconds;
        var whackTime = 0.12f;
        var settleTime = 0.3f;
        var total = cockTime + whackTime + settleTime;

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(total),
            AnimationTracks =
            {
                // Slowly cock the rake, then snap it down onto the victim.
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Angle.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(-35f), cockTime),
                        new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(15f), cockTime + whackTime),
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
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, cockTime),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0f, 0.25f), cockTime + whackTime),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, total),
                    },
                },
            },
        };

        _animation.Play((ent, animComp), animation, WhackKey);
    }
}
