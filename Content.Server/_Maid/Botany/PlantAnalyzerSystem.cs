using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Shared._Maid.Botany;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Botany;

/// <summary>
///     Plant analyzer for maid's server-side botany. The window layout is taken from wizden,
///     but here the server reads <see cref="PlantHolderComponent"/> / <see cref="SeedData"/> and sends everything
///     in <see cref="PlantAnalyzerState"/>.
/// </summary>
public sealed class PlantAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    /// <summary>
    ///     Seed prototypes by their name, to find the unmodified seed for the trait deltas.
    ///     Seeds in trays are clones that don't keep the prototype id.
    /// </summary>
    private readonly Dictionary<string, SeedPrototype> _seedsByName = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlantAnalyzerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PlantAnalyzerComponent, PlantAnalyzerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PlantAnalyzerComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<PlantAnalyzerComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        CacheSeeds();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PlantAnalyzerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Target is not { } target || comp.User is not { } user || _timing.CurTime < comp.NextUpdate)
                continue;

            comp.NextUpdate = _timing.CurTime + comp.UpdateInterval;

            if (Deleted(target) || !_interaction.InRangeUnobstructed(user, target))
            {
                Stop((uid, comp));
                continue;
            }

            UpdateUi((uid, comp), target);
        }
    }

    private void OnAfterInteract(Entity<PlantAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach || !CanAnalyze(target))
            return;

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.User,
            ent.Comp.ScanDelay,
            new PlantAnalyzerDoAfterEvent(),
            ent,
            target: target,
            used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDoAfter(Entity<PlantAnalyzerComponent> ent, ref PlantAnalyzerDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } target || !CanAnalyze(target))
            return;

        args.Handled = true;

        _audio.PlayPvs(ent.Comp.ScanningEndSound, ent);

        ent.Comp.Target = target;
        ent.Comp.User = args.Args.User;
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;

        _ui.OpenUi(ent.Owner, PlantAnalyzerUiKey.Key, args.Args.User);
        UpdateUi(ent, target);
    }

    private void OnUiClosed(Entity<PlantAnalyzerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(PlantAnalyzerUiKey.Key) || args.Actor != ent.Comp.User)
            return;

        ent.Comp.Target = null;
        ent.Comp.User = null;
    }

    private void OnUnequipped(Entity<PlantAnalyzerComponent> ent, ref GotUnequippedHandEvent args)
    {
        Stop(ent);
    }

    private void Stop(Entity<PlantAnalyzerComponent> ent)
    {
        if (ent.Comp.User is { } user)
            _ui.CloseUi(ent.Owner, PlantAnalyzerUiKey.Key, user);

        ent.Comp.Target = null;
        ent.Comp.User = null;
    }

    private bool CanAnalyze(EntityUid target)
    {
        return HasComp<PlantHolderComponent>(target) || HasComp<SeedComponent>(target) || HasComp<ProduceComponent>(target);
    }

    private void UpdateUi(Entity<PlantAnalyzerComponent> ent, EntityUid target)
    {
        if (BuildState(target) is { } state)
            _ui.SetUiState(ent.Owner, PlantAnalyzerUiKey.Key, state);
    }

    private PlantAnalyzerState? BuildState(EntityUid target)
    {
        PlantHolderComponent? holder = null;
        SeedData? seed = null;

        if (TryComp(target, out holder))
            seed = holder.Seed;
        else if (TryComp<SeedComponent>(target, out var seedComp))
            _botany.TryGetSeed(seedComp, out seed);
        else if (TryComp<ProduceComponent>(target, out var produce))
            _botany.TryGetSeed(produce, out seed);
        else
            return null;

        var state = new PlantAnalyzerState
        {
            Target = GetNetEntity(target),
            Name = seed != null ? Loc.GetString(seed.DisplayName) : Name(target),
            HasSeed = seed != null,
        };

        if (holder != null)
            state.Warnings = GetWarnings(holder);

        if (seed == null)
            return state;

        state.Traits = GetTraits(seed);
        if (_seedsByName.TryGetValue(seed.Name, out var prototype))
            state.PrototypeTraits = GetTraits(prototype);

        // Read into a local first: calling a method on the field counts as "execute" access to SeedData.
        var harvestRepeat = seed.HarvestRepeat;
        state.HarvestType = harvestRepeat.ToString();
        state.IdealHeat = seed.IdealHeat;
        state.HeatTolerance = seed.HeatTolerance;
        state.LowPressureTolerance = seed.LowPressureTolerance;
        state.HighPressureTolerance = seed.HighPressureTolerance;
        state.WaterConsumption = seed.WaterConsumption;
        state.NutrientConsumption = seed.NutrientConsumption;
        state.ConsumedGases = new(seed.ConsumeGasses);
        state.ExudedGases = new(seed.ExudeGasses);
        state.Chemicals = GetChemicals(seed);
        state.Mutations = GetMutations(seed);

        if (holder != null)
        {
            state.Holder = new PlantAnalyzerHolderStatus
            {
                Dead = holder.Dead,
                ReadyForHarvest = holder.Harvest,
                Health = holder.Health,
                Age = holder.Age,
                GrowthStage = GetGrowthStage(holder, seed),
                WaterLevel = holder.WaterLevel,
                NutritionLevel = holder.NutritionLevel,
                MutationLevel = holder.MutationLevel,
                MutationMod = holder.MutationMod,
            };
        }

        return state;
    }

    private static PlantAnalyzerTraits GetTraits(SeedData seed)
    {
        return new PlantAnalyzerTraits
        {
            Endurance = seed.Endurance,
            Yield = seed.Yield,
            Lifespan = seed.Lifespan,
            Maturation = seed.Maturation,
            Production = seed.Production,
            Potency = seed.Potency,
            GrowthStages = seed.GrowthStages,
        };
    }

    /// <summary>
    ///     Same formula as <c>PlantHolderSystem.GetCurrentGrowthStage</c>, which is private.
    /// </summary>
    private static int GetGrowthStage(PlantHolderComponent holder, SeedData seed)
    {
        if (seed.Maturation <= 0)
            return seed.GrowthStages;

        return Math.Clamp((int) (holder.Age * seed.GrowthStages / seed.Maturation), 1, seed.GrowthStages);
    }

    /// <summary>
    ///     Same amounts as <c>BotanySystem.ProduceGrown</c> puts into the produce.
    /// </summary>
    private static Dictionary<string, float> GetChemicals(SeedData seed)
    {
        var chemicals = new Dictionary<string, float>();
        foreach (var (reagent, quantity) in seed.Chemicals)
        {
            var amount = (float) quantity.Min;
            if (quantity.PotencyDivisor > 0 && seed.Potency > 0)
                amount += seed.Potency / quantity.PotencyDivisor;

            amount = Math.Clamp(amount, quantity.Min, quantity.Max);
            if (amount > 0f)
                chemicals[reagent] = amount;
        }

        return chemicals;
    }

    private List<string> GetMutations(SeedData seed)
    {
        var mutations = new List<string>();
        if (seed.Seedless)
            mutations.Add(Loc.GetString("plant-analyzer-mutation-seedless"));
        if (!seed.Viable)
            mutations.Add(Loc.GetString("plant-analyzer-mutation-unviable"));
        if (seed.Ligneous)
            mutations.Add(Loc.GetString("plant-analyzer-mutation-ligneous"));
        if (seed.TurnIntoKudzu)
            mutations.Add(Loc.GetString("plant-analyzer-mutation-kudzu"));
        if (seed.CanScream)
            mutations.Add(Loc.GetString("plant-analyzer-mutation-scream"));

        return mutations;
    }

    /// <summary>
    ///     The tray examine warnings, shown even if the tray doesn't draw warning lights.
    /// </summary>
    private List<string> GetWarnings(PlantHolderComponent holder)
    {
        var warnings = new List<string>();

        if (holder.Seed == null)
        {
            warnings.Add(Loc.GetString("plant-holder-component-nothing-planted-message"));
            return warnings;
        }

        if (holder.Dead)
            warnings.Add(Loc.GetString("plant-holder-component-dead-plant-matter-message"));
        else if (holder.Health <= holder.Seed.Endurance / 2)
            warnings.Add(Loc.GetString("plant-analyzer-warning-low-health"));

        if (holder.WeedLevel >= 5)
            warnings.Add(Loc.GetString("plant-holder-component-weed-high-level-message"));
        if (holder.PestLevel >= 5)
            warnings.Add(Loc.GetString("plant-holder-component-pest-high-level-message"));
        if (holder.Toxins > 40f)
            warnings.Add(Loc.GetString("plant-holder-component-toxins-high-warning"));
        if (holder.ImproperLight)
            warnings.Add(Loc.GetString("plant-holder-component-light-improper-warning"));
        if (holder.ImproperHeat)
            warnings.Add(Loc.GetString("plant-holder-component-heat-improper-warning"));
        if (holder.ImproperPressure)
            warnings.Add(Loc.GetString("plant-holder-component-pressure-improper-warning"));
        if (holder.MissingGas > 0)
            warnings.Add(Loc.GetString("plant-holder-component-gas-missing-warning"));

        return warnings;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<SeedPrototype>())
            CacheSeeds();
    }

    private void CacheSeeds()
    {
        _seedsByName.Clear();
        foreach (var seed in _proto.EnumeratePrototypes<SeedPrototype>())
        {
            _seedsByName.TryAdd(seed.Name, seed);
        }
    }
}
