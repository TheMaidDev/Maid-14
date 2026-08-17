using Content.Server._Maid.Terminator.Components;
using Content.Server.Mind;
using Content.Shared.Body.Events;

namespace Content.Server._Maid.Terminator.EntitySystems;

// SpawnOnDespawnSystem will not do the trick cause no easy way to transfer mind without changing SpawnOnDespawnSystem itself
public sealed class SpawnOnGibSystem : EntitySystem
{
    [Dependency] private MindSystem _mindSystem = default!;
    
    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<SpawnOnGibComponent, BeingGibbedEvent>(OnGibbed);
    }

    private void OnGibbed(Entity<SpawnOnGibComponent> ent, ref BeingGibbedEvent args)
    {
        if (!TryComp(ent, out TransformComponent? xform)) return;
        
        if (ent.Comp.Prototype == null) return;
        
        var spawned = Spawn(ent.Comp.Prototype, xform.Coordinates);

        if (ent.Comp.TransferMind && _mindSystem.TryGetMind(ent, out var mindId, out var mindComponent))
        {
            _mindSystem.TransferTo(mindId, spawned);
        }
    }
}