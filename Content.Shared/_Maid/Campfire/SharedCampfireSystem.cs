using Content.Shared.Examine;
using Content.Shared.Temperature;

namespace Content.Shared._Maid.Campfire;

public abstract class SharedCampfireSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CampfireComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CampfireComponent, IsHotEvent>(OnIsHot);
    }

    private void OnExamined(Entity<CampfireComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.Lit ? "campfire-examine-lit" : "campfire-examine-unlit"));

        var fuel = ent.Comp.FuelLevel switch
        {
            CampfireFuelLevel.Embers => "campfire-examine-time-embers",
            CampfireFuelLevel.Low => "campfire-examine-time-low",
            CampfireFuelLevel.Medium => "campfire-examine-time-medium",
            CampfireFuelLevel.High => "campfire-examine-time-high",
            CampfireFuelLevel.Full => "campfire-examine-time-full",
            _ => "campfire-examine-fuel-empty",
        };
        args.PushMarkup(Loc.GetString(fuel));
    }

    // A burning fire can light cigarettes, torches and so on.
    private void OnIsHot(Entity<CampfireComponent> ent, ref IsHotEvent args)
    {
        args.IsHot |= ent.Comp.Lit;
    }

    /// <summary>
    /// Fuel stage of the fire: 0 is the smallest, the last is the biggest. The stages are equal parts of the
    /// fuel cap and are as many as there are air heating powers.
    /// </summary>
    public static int GetStage(CampfireComponent comp)
    {
        var stages = comp.AirHeatPowers.Count;
        if (stages == 0 || comp.MaxFuel <= 0f)
            return 0;

        return Math.Clamp((int) (comp.Fuel / comp.MaxFuel * stages), 0, stages - 1);
    }
}
