using Content.Server.Popups;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Animal;

public sealed class AnimalDraggableSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private Dictionary<EntityUid, float> _stamina = new();

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<AnimalDraggableComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<AnimalDraggableComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<AnimalDraggableComponent, PullStoppedMessage>(OnPullStopped);
    }

    private void OnPullAttempt(EntityUid uid, AnimalDraggableComponent component, PullAttemptEvent args)
    {
        if (args.PullerUid != uid) 
            return;

        var weight = GetWeight(args.PulledUid);
        
        if (weight > component.MaxDragWeight)
        {
            _popup.PopupEntity($"{Name(uid)} пытается тащить, но слишком тяжело!", uid, uid);
            return;
        }

        if (!component.CanDragMobs && HasComp<MobStateComponent>(args.PulledUid))
        {
            _popup.PopupEntity($"{Name(uid)} не может таскать людей!", uid, uid);
            return;
        }

        if (component.DraggingEntity != null)
        {
            _popup.PopupEntity($"{Name(uid)} уже что-то тащит!", uid, uid);
        }
    }

    private void OnPullStarted(EntityUid uid, AnimalDraggableComponent component, PullStartedMessage args)
    {
        if (args.PullerUid != uid) 
            return;

        component.DraggingEntity = args.PulledUid;
        
        var animalName = Name(uid);
        var targetName = Name(args.PulledUid);
        
        _popup.PopupEntity($"{animalName} схватил {targetName} зубами и потащил!", uid, uid);
        
        if (!_stamina.ContainsKey(uid))
            _stamina[uid] = 0;
    }

    private void OnPullStopped(EntityUid uid, AnimalDraggableComponent component, PullStoppedMessage args)
    {
        if (args.PullerUid != uid) 
            return;

        component.DraggingEntity = null;
        
        var animalName = Name(uid);
        _popup.PopupEntity($"{animalName} бросил ношу", uid, uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var toRemove = new List<EntityUid>();
        
        foreach (var uid in _stamina.Keys)
        {
            if (!TryComp<AnimalDraggableComponent>(uid, out var component))
            {
                toRemove.Add(uid);
                continue;
            }

            if (component.DraggingEntity == null || !component.RequiresEffort)
            {
                _stamina[uid] -= 10 * frameTime;
                if (_stamina[uid] < 0)
                    _stamina[uid] = 0;
                continue;
            }

            var weight = GetWeight(component.DraggingEntity.Value);
            var penalty = weight / component.MaxDragWeight;
            var drain = component.StaminaDrainPerSecond * frameTime * (1 + penalty);
            
            _stamina[uid] += drain;
            
            if (_stamina[uid] > 100)
            {
                var animalName = Name(uid);
                _popup.PopupEntity($"{animalName} выдохся и бросил ношу!", uid, uid);
                
                if (TryComp<PullableComponent>(uid, out var pullable))
                {
                    pullable.Puller = null;
                }
                
                component.DraggingEntity = null;
                _stamina[uid] = 80;
            }
        }

        foreach (var uid in toRemove)
        {
            _stamina.Remove(uid);
        }
    }

    private float GetWeight(EntityUid uid)
    {
        return _physicsQuery.TryComp(uid, out var physics) ? physics.Mass : 10f;
    }

    private string Name(EntityUid uid)
    {
        return TryComp<MetaDataComponent>(uid, out var meta) ? meta.EntityName : "существо";
    }
}