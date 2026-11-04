using System.Numerics;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Maid.Movement;

/// <summary>
/// Stops pulling a thing that got stuck, instead of letting the pull joint drag it through the obstacle.
/// Mostly happens with things the puller walks through but the pulled thing bumps into,
/// like a machine pulled over a bench: it stuck for a moment and then popped out on the other side.
/// </summary>
public sealed class PullStuckSystem : EntitySystem
{
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// How long a thing has to stay stuck before the pull breaks, in seconds.
    /// </summary>
    private const float StuckTime = 0.2f;

    /// <summary>
    /// The pull joint keeps the thing at most this much farther than its max length, anything more means it's held back.
    /// </summary>
    private const float MaxLag = 0.5f;

    /// <summary>
    /// How close to the max length of the joint the thing has to be for the pull to be taut.
    /// </summary>
    private const float TautSlack = 0.05f;

    /// <summary>
    /// Cosine of the angle in which an obstacle counts as being between the thing and the puller.
    /// </summary>
    private const float BlockingCos = 0.3f;

    private Dictionary<EntityUid, float> _stuck = new();
    private Dictionary<EntityUid, float> _stuckNext = new();
    private readonly List<(EntityUid Puller, EntityUid Pulled, PullableComponent Pullable)> _toStop = new();

    private EntityQuery<JointComponent> _jointQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<PullableComponent> _pullableQuery;

    public override void Initialize()
    {
        base.Initialize();

        _jointQuery = GetEntityQuery<JointComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActivePullerComponent, PullerComponent, PhysicsComponent>();
        while (query.MoveNext(out var puller, out _, out var pullerComp, out var pullerBody))
        {
            if (pullerComp.Pulling is not { } pulled ||
                !_pullableQuery.TryComp(pulled, out var pullable) ||
                HasComp<MobStateComponent>(pulled) || // grabs are a different story
                !IsStuck(puller, pullerBody, pulled, pullable))
            {
                continue;
            }

            var time = _stuck.GetValueOrDefault(pulled) + frameTime;
            if (time < StuckTime)
            {
                _stuckNext[pulled] = time;
                continue;
            }

            _toStop.Add((puller, pulled, pullable));
        }

        // Only the pulls that are still stuck keep their timer.
        (_stuck, _stuckNext) = (_stuckNext, _stuck);
        _stuckNext.Clear();

        foreach (var (puller, pulled, pullable) in _toStop)
        {
            if (_pulling.TryStopPull(pulled, pullable, ignoreGrab: true))
                _popup.PopupEntity(Loc.GetString("pull-stuck-popup", ("pulled", pulled)), pulled, puller);
        }

        _toStop.Clear();
    }

    private bool IsStuck(EntityUid puller, PhysicsComponent pullerBody, EntityUid pulled, PullableComponent pullable)
    {
        if (pullable.PullJointId is not { } jointId ||
            !_jointQuery.TryComp(pulled, out var joints) ||
            !joints.GetJoints.TryGetValue(jointId, out var joint) ||
            joint is not DistanceJoint distanceJoint)
        {
            return false;
        }

        var pulledPos = _transform.GetWorldPosition(pulled);
        var toPuller = _transform.GetWorldPosition(puller) - pulledPos;
        var distance = toPuller.Length();

        // Held back far behind the joint limit by anything.
        if (distance > distanceJoint.MaxLength + MaxLag)
            return true;

        // The pull isn't taut, nothing is dragging the thing into anything.
        if (distance < distanceJoint.MaxLength - TautSlack || distance < 0.01f)
            return false;

        var dir = toPuller / distance;
        var contacts = _physics.GetContacts(pulled);
        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching || !contact.Hard)
                continue;

            var other = contact.EntityA == pulled ? contact.EntityB : contact.EntityA;
            if (other == puller || !_physicsQuery.TryComp(other, out var otherBody))
                continue;

            // The puller bumps into it too, so it can't be walked through: leave it to the usual physics.
            if ((pullerBody.CollisionMask & otherBody.CollisionLayer) != 0 ||
                (otherBody.CollisionMask & pullerBody.CollisionLayer) != 0)
            {
                continue;
            }

            // The obstacle is between the thing and the puller, not just brushed on the side.
            var toOther = _transform.GetWorldPosition(other) - pulledPos;
            if (toOther.LengthSquared() < 0.0001f || Vector2.Dot(Vector2.Normalize(toOther), dir) > BlockingCos)
                return true;
        }

        return false;
    }
}
