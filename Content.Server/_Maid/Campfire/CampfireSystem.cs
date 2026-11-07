using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.Components;
using Content.Server.Stack;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Maid.Campfire;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Audio;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Campfire;

public sealed class CampfireSystem : SharedCampfireSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    private readonly HashSet<Entity<TemperatureComponent>> _cooking = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CampfireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CampfireComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CampfireComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CampfireComponent, CampfireStompDoAfterEvent>(OnStompDoAfter);
        SubscribeLocalEvent<CampfireComponent, ReactionEntityEvent>(OnReaction);
    }

    private void OnMapInit(Entity<CampfireComponent> ent, ref MapInitEvent args)
    {
        UpdateFuelLevel(ent);
        ApplyLitState(ent);
    }

    private void OnInteractUsing(Entity<CampfireComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var comp = ent.Comp;

        // Anything hot (lighter, welder, lit match) lights a fed fire.
        if (!comp.Lit)
        {
            var hot = new IsHotEvent();
            RaiseLocalEvent(args.Used, hot);

            if (hot.IsHot)
            {
                args.Handled = true;

                if (comp.Fuel <= 0f)
                {
                    _popup.PopupEntity(Loc.GetString("campfire-no-fuel"), ent, args.User);
                    return;
                }

                SetLit(ent, true);
                _popup.PopupEntity(Loc.GetString("campfire-ignite", ("user", args.User), ("fire", ent)),
                    ent,
                    PopupType.Medium);
                return;
            }
        }

        // Water from a bucket or glass puts a lit fire out.
        if (comp.Lit && TryPour(ent, args.User, args.Used))
        {
            args.Handled = true;
            return;
        }

        if (!TryGetFuelValue(ent, args.Used, out var seconds))
            return; // not fuel: fall through so the item can be placed on the fire

        args.Handled = true;

        if (comp.Fuel >= comp.MaxFuel)
        {
            _popup.PopupEntity(Loc.GetString("campfire-full", ("fire", ent)), ent, args.User);
            return;
        }

        comp.Fuel = Math.Min(comp.MaxFuel, comp.Fuel + seconds);
        UpdateFuelLevel(ent);

        _popup.PopupEntity(Loc.GetString("campfire-add-fuel", ("user", args.User), ("item", args.Used)),
            ent,
            PopupType.Small);

        if (TryComp<StackComponent>(args.Used, out var stack))
            _stack.Use(args.Used, 1, stack);
        else
            QueueDel(args.Used);
    }

    private void OnGetVerbs(Entity<CampfireComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.Lit)
            return;

        var uid = ent.Owner;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("campfire-verb-stomp"),
            Act = () => StartStomp((uid, ent.Comp), user),
        });
    }

    /// <summary>
    /// Stamping out: small and medium fires take a few seconds, a big one just sets the person alight.
    /// </summary>
    public void StartStomp(Entity<CampfireComponent> ent, EntityUid user)
    {
        if (!ent.Comp.Lit)
            return;

        var stage = GetStage(ent.Comp);
        if (stage >= ent.Comp.StompDelays.Count)
        {
            BurnStomper(ent, user);
            return;
        }

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.StompDelays[stage], new CampfireStompDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("campfire-stomp-start", ("user", user), ("fire", ent)), ent, PopupType.Small);
    }

    private void OnStompDoAfter(Entity<CampfireComponent> ent, ref CampfireStompDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Lit)
            return;

        args.Handled = true;

        // The fire may have grown meanwhile (someone added fuel).
        if (GetStage(ent.Comp) >= ent.Comp.StompDelays.Count)
        {
            BurnStomper(ent, args.User);
            return;
        }

        SetLit(ent, false);
        _popup.PopupEntity(Loc.GetString("campfire-stomped", ("user", args.User), ("fire", ent)), ent, PopupType.Small);
    }

    private void BurnStomper(Entity<CampfireComponent> ent, EntityUid user)
    {
        _flammable.AdjustFireStacks(user, ent.Comp.StompBurnFireStacks, ignite: true);
        _popup.PopupEntity(Loc.GetString("campfire-stomp-burned", ("user", user), ("fire", ent)), user, user, PopupType.LargeCaution);
    }

    /// <summary>
    /// Pours the extinguishing reagent of a container onto the fire. Returns true when the item was a container
    /// with such a reagent (the interaction is then used up, even if there was not enough of it).
    /// </summary>
    private bool TryPour(Entity<CampfireComponent> ent, EntityUid user, EntityUid used)
    {
        // Extinguishers and spray bottles are sprayed, not poured.
        if (HasComp<SprayComponent>(used))
            return false;

        if (!_solutions.TryGetDrainableSolution(used, out var soln, out var solution))
            return false;

        var amount = FixedPoint2.New(ent.Comp.DouseAmount);
        var available = FixedPoint2.Zero;
        foreach (var reagent in ent.Comp.ExtinguishReagents)
            available += solution.GetReagentQuantity(new ReagentId(reagent, null));

        if (available <= FixedPoint2.Zero)
            return false;

        if (available < amount)
        {
            _popup.PopupEntity(Loc.GetString("campfire-not-enough-water", ("amount", ent.Comp.DouseAmount)),
                ent,
                user,
                PopupType.MediumCaution);
            return true;
        }

        var left = amount;
        foreach (var reagent in ent.Comp.ExtinguishReagents)
        {
            if (left <= FixedPoint2.Zero)
                break;

            left -= _solutions.RemoveReagent(soln.Value, new ReagentId(reagent, null), left);
        }

        Douse(ent);
        return true;
    }

    private void Douse(Entity<CampfireComponent> ent)
    {
        var stage = GetStage(ent.Comp);
        SetLit(ent, false);
        _popup.PopupEntity(Loc.GetString("campfire-doused", ("fire", ent)), ent, PopupType.Medium);
        _audio.PlayPvs(ent.Comp.ExtinguishSound, ent);
        ReleaseSteam(ent, stage);
    }

    /// <summary>
    /// Hot water vapor puffs into the air of the tile when a fire is put out with a liquid; the bigger the fire,
    /// the more of it. Nothing happens on tiles without real air.
    /// </summary>
    private void ReleaseSteam(Entity<CampfireComponent> ent, int stage)
    {
        var comp = ent.Comp;
        if (comp.SteamMoles.Count == 0)
            return;

        var moles = comp.SteamMoles[Math.Clamp(stage, 0, comp.SteamMoles.Count - 1)];
        if (moles <= 0f)
            return;

        var mixture = _atmosphere.GetTileMixture((ent.Owner, Transform(ent)), excite: true);
        if (mixture == null || mixture.Immutable)
            return;

        var steam = new GasMixture(new float[Atmospherics.AdjustedNumberOfGases], comp.SteamTemperature);
        steam.AdjustMoles(Gas.WaterVapor, moles);
        _atmosphere.Merge(mixture, steam);
    }

    /// <summary>
    /// Water landing on the fire: a big splash douses it at once, smaller amounts (extinguisher and
    /// spray particles) count as sprays.
    /// </summary>
    private void OnReaction(Entity<CampfireComponent> ent, ref ReactionEntityEvent args)
    {
        var comp = ent.Comp;
        if (!comp.Lit || args.Method != ReactionMethod.Touch || !comp.ExtinguishReagents.Contains(args.Reagent.ID))
            return;

        if ((float) args.ReagentQuantity.Quantity >= comp.DouseAmount)
        {
            Douse(ent);
            return;
        }

        var now = _timing.CurTime;
        if (comp.SprayHits > 0 && now - comp.LastSprayHit > TimeSpan.FromSeconds(comp.SprayMemory))
            comp.SprayHits = 0;

        // Several particles of one spray land one after another: count them once.
        if (comp.SprayHits > 0 && now - comp.LastSprayHit < TimeSpan.FromSeconds(comp.SpraySpacing))
            return;

        comp.LastSprayHit = now;
        comp.SprayHits++;

        if (GetStage(comp) < comp.StompDelays.Count)
        {
            Douse(ent); // small and medium fires go out at once
            return;
        }

        if (comp.SprayHits == 1)
            comp.SpraysRequired = _random.Next(comp.MinSpraysForLargeFire, comp.MaxSpraysForLargeFire + 1);

        if (comp.SprayHits >= comp.SpraysRequired)
        {
            comp.SprayHits = 0;
            Douse(ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("campfire-sprayed", ("fire", ent)), ent, PopupType.Small);
    }

    /// <summary>
    /// Burn time granted by one unit of <paramref name="item"/> (a single piece of a stack, or the whole item).
    /// Items made of fuel materials burn by their material amounts; items with no material composition
    /// at all burn only if they carry one of the fuel tags.
    /// </summary>
    private bool TryGetFuelValue(Entity<CampfireComponent> campfire, EntityUid item, out float seconds)
    {
        seconds = 0f;
        var comp = campfire.Comp;

        if (HasComp<MobStateComponent>(item) || _whitelist.IsBlacklistPass(comp.FuelBlacklist, item))
            return false;

        if (TryComp<PhysicalCompositionComponent>(item, out var composition) && composition.MaterialComposition.Count > 0)
        {
            foreach (var (material, amount) in composition.MaterialComposition)
            {
                if (comp.FuelPerMaterial.TryGetValue(material, out var perUnit))
                    seconds += perUnit * amount;
            }

            return seconds > 0f;
        }

        foreach (var (tag, perItem) in comp.FuelTags)
        {
            if (perItem > seconds && _tag.HasTag(item, tag))
                seconds = perItem;
        }

        return seconds > 0f;
    }

    public void SetLit(Entity<CampfireComponent> ent, bool lit)
    {
        if (ent.Comp.Lit == lit)
            return;

        ent.Comp.Lit = lit;
        ent.Comp.OxygenlessTime = 0f;
        ent.Comp.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.UpdateInterval);
        Dirty(ent);
        ApplyLitState(ent);
    }

    private void ApplyLitState(Entity<CampfireComponent> ent)
    {
        _appearance.SetData(ent, CampfireVisuals.Lit, ent.Comp.Lit);
        _lights.SetEnabled(ent, ent.Comp.Lit);
        _ambient.SetAmbience(ent, ent.Comp.Lit);
    }

    private void UpdateFuelLevel(Entity<CampfireComponent> ent)
    {
        var ratio = ent.Comp.MaxFuel > 0f ? ent.Comp.Fuel / ent.Comp.MaxFuel : 0f;
        var level = ent.Comp.Fuel <= 0f
            ? CampfireFuelLevel.Empty
            : ratio >= 0.8f
                ? CampfireFuelLevel.Full
                : ratio >= 0.6f
                    ? CampfireFuelLevel.High
                    : ratio >= 0.4f
                        ? CampfireFuelLevel.Medium
                        : ratio >= 0.2f
                            ? CampfireFuelLevel.Low
                            : CampfireFuelLevel.Embers;

        if (ent.Comp.FuelLevel == level)
            return;

        ent.Comp.FuelLevel = level;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<CampfireComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!comp.Lit || curTime < comp.NextUpdate)
                continue;

            var ent = (uid, comp);
            var dt = comp.UpdateInterval;
            comp.NextUpdate = curTime + TimeSpan.FromSeconds(dt);

            // No air, no fire.
            if (HasOxygen((uid, xform), comp))
            {
                comp.OxygenlessTime = 0f;
            }
            else
            {
                comp.OxygenlessTime += dt;
                if (comp.OxygenlessTime > comp.MaxOxygenlessTime)
                {
                    SetLit(ent, false);
                    _popup.PopupEntity(Loc.GetString("campfire-suffocated", ("fire", uid)), uid, PopupType.Small);
                    continue;
                }
            }

            comp.Fuel = Math.Max(0f, comp.Fuel - dt);
            UpdateFuelLevel(ent);

            HeatItems(comp, xform, dt);
            HeatAir((uid, xform), comp, dt);

            if (comp.Fuel <= 0f)
            {
                SetLit(ent, false);
                _popup.PopupEntity(Loc.GetString("campfire-burned-out", ("fire", uid)), uid, PopupType.Small);
            }
        }
    }

    private void HeatItems(CampfireComponent comp, TransformComponent xform, float dt)
    {
        _cooking.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, comp.CookRange, _cooking, LookupFlags.Dynamic | LookupFlags.Sundries);

        var heated = 0;
        foreach (var item in _cooking)
        {
            // Only loose items cook; people standing nearby are not roasted by this.
            if (!HasComp<ItemComponent>(item))
                continue;

            if (heated++ >= comp.MaxCookedItems)
                break;

            if (item.Comp.CurrentTemperature >= comp.MaxItemTemperature)
                continue;

            _temperature.ChangeHeat(item, comp.HeatPower * dt, temperature: item.Comp);
        }
    }

    /// <summary>
    /// Warms the air of the fire's tile; the atmos system then spreads it to the neighbours.
    /// The power depends on how much fuel is left, and the fire stops adding heat past a ceiling.
    /// </summary>
    private void HeatAir(Entity<TransformComponent> ent, CampfireComponent comp, float dt)
    {
        if (comp.AirHeatPowers.Count == 0 || comp.MaxFuel <= 0f)
            return;

        var power = comp.AirHeatPowers[GetStage(comp)];
        if (power <= 0f)
            return;

        var mixture = _atmosphere.GetTileMixture((ent.Owner, ent.Comp), excite: true);
        if (mixture == null || mixture.Immutable || mixture.Temperature >= comp.AirHeatMaxTemperature)
            return;

        // Thin air has next to no heat capacity, so never push it past the ceiling in a single step.
        var headroom = (comp.AirHeatMaxTemperature - mixture.Temperature) * _atmosphere.GetHeatCapacity(mixture, true);
        _atmosphere.AddHeat(mixture, Math.Min(power * dt, headroom));
    }

    private bool HasOxygen(Entity<TransformComponent> ent, CampfireComponent comp)
    {
        var mixture = _atmosphere.GetTileMixture((ent.Owner, ent.Comp));
        return mixture != null && mixture.GetMoles(Gas.Oxygen) >= comp.MinOxygenMoles;
    }
}
