using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;
using Robust.Shared.Random;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._Maid.RandomExplodeOnHit;

public sealed class RandomExplodeOnHitSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomExplodeOnHitComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<RandomExplodeOnHitComponent, AttackedEvent>(OnAttacked);
    }

    private void OnMeleeHit(Entity<RandomExplodeOnHitComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count <= 0)
            return;
        RegisterHit(ent);
    }

    private void Explode(Entity<RandomExplodeOnHitComponent> ent)
    {
        var comp = ent.Comp;

        var fill = 0f;
        if (_solution.TryGetSolution(ent.Owner, comp.Solution, out var soln, out var solution) && solution.MaxVolume > 0)
        {
            fill = solution.Volume.Float() / solution.MaxVolume.Float();

            var spilled = _solution.SplitSolution(soln.Value, solution.Volume);
            _puddle.TrySpillAt(ent.Owner, spilled, out _);
        }

        var power = Math.Clamp((fill - comp.MinFill) / (1f - comp.MinFill), 0f, 1f);

        SpawnNextToOrDrop(comp.BrokenPrototype, ent.Owner);

        _explosion.TriggerExplosive(ent.Owner, totalIntensity: power * comp.MaxTotalIntensity);
    }

    private void OnAttacked(Entity<RandomExplodeOnHitComponent> ent, ref AttackedEvent args)
    {
        RegisterHit(ent);
    }

    private void RegisterHit(Entity<RandomExplodeOnHitComponent> ent)
    {
        var now = _timing.CurTime;
        var comp = ent.Comp;

        if (comp.LastHit != null && now - comp.LastHit > comp.TimeBetweenHits)
            comp.HitCount = 0;

        comp.HitCount++;
        comp.LastHit = now;

        if (comp.HitCount <= comp.CriticalHitCount)
            return;

        var chance = Math.Min(1f, comp.ExplodeChance * (comp.HitCount - comp.CriticalHitCount));

        if (_random.Prob(chance))
            Explode(ent);
    }
}
