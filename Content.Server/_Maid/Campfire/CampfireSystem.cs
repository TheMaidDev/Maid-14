using Content.Server.Atmos.EntitySystems;
using Content.Server.Stack;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Maid.Campfire;
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
using Robust.Shared.Timing;

namespace Content.Server._Maid.Campfire;

public sealed class CampfireSystem : SharedCampfireSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
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
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("campfire-verb-extinguish"),
            Act = () => SetLit((uid, ent.Comp), false),
        });
    }

    /// <summary>
    /// Burn time granted by one unit of <paramref name="item"/> (a single piece of a stack, or the whole item).
    /// Items made of fuel materials burn by their material amounts; items with no material composition
    /// at all burn only if they carry one of the junk tags.
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
        var stages = comp.AirHeatPowers.Count;
        if (stages == 0 || comp.MaxFuel <= 0f)
            return;

        var stage = Math.Clamp((int) (comp.Fuel / comp.MaxFuel * stages), 0, stages - 1);
        var power = comp.AirHeatPowers[stage];
        if (power <= 0f)
            return;

        var mixture = _atmosphere.GetTileMixture((ent.Owner, ent.Comp), excite: true);
        if (mixture == null || mixture.Immutable || mixture.Temperature >= comp.AirHeatMaxTemperature)
            return;

        // Thin air has next to no heat capacity, so never push it past the ceiling in a single step.
        var headroom = (comp.AirHeatMaxTemperature - mixture.Temperature) * _atmosphere.GetHeatCapacity(mixture, true);
        _atmosphere.AddHeat(mixture, Math.Min(power * dt, headroom));
    }
}
