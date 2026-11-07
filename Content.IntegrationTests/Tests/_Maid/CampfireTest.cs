using Content.Goobstation.Maths.FixedPoint;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Server._Maid.Campfire;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry;
using Content.Shared.Interaction;
using Content.Shared.Temperature.Components;
using Content.Shared._Maid.Campfire;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting;
using System.Linq;

namespace Content.IntegrationTests.Tests._Maid;

[TestFixture]
public sealed class CampfireTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestCampfireHotItem
  components:
  - type: AlwaysHot
";

    private static async Task AddAir(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid mapUid)
    {
        await server.WaitPost(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            server.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
        });
    }

    private static void Use(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid user, EntityUid used, EntityUid target)
    {
        var ev = new InteractUsingEvent(user, used, target, new EntityCoordinates(target, 0, 0));
        server.EntMan.EventBus.RaiseLocalEvent(target, ev);
    }

    [Test]
    public async Task FedAndLitCampfireCooksFood()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);

        EntityUid fire = default;
        EntityUid user = default;
        EntityUid meat = default;

        await server.WaitAssertion(() =>
        {
            fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            Assert.That(comp.Lit, Is.False);

            // No fuel yet: fire cannot be lit.
            var hot = server.EntMan.SpawnEntity("TestCampfireHotItem", map.GridCoords);
            Use(server, user, hot, fire);
            Assert.That(comp.Lit, Is.False);

            // Feeding consumes the planks.
            for (var i = 0; i < 3; i++)
            {
                var plank = server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords);
                Use(server, user, plank, fire);
                Assert.That(server.EntMan.Deleted(plank) || server.EntMan.IsQueuedForDeletion(plank), Is.True);
            }

            Assert.That(comp.Fuel, Is.GreaterThan(0f));

            // Non-fuel is not consumed.
            var junk = server.EntMan.SpawnEntity("TestCampfireHotItem", map.GridCoords);
            var fuelBefore = comp.Fuel;
            Use(server, user, junk, fire);
            Assert.That(comp.Fuel, Is.EqualTo(fuelBefore));

            Use(server, user, hot, fire);
            Assert.That(comp.Lit, Is.True);

            meat = server.EntMan.SpawnEntity("FoodMeat", server.EntMan.GetComponent<TransformComponent>(fire).Coordinates);
        });

        // 3 planks burn for about 3 minutes.
        await server.WaitRunTicks(30 * 150);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(meat), Is.True, "raw meat should have been replaced by cooked meat");
            var cooked = server.EntMan.EntityQuery<MetaDataComponent>()
                .Count(m => m.EntityPrototype?.ID == "FoodMeatCooked");
            Assert.That(cooked, Is.GreaterThan(0));
        });

        // Fuel eventually runs out and the fire goes out.
        await server.WaitRunTicks(30 * 120);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            Assert.That(comp.Lit, Is.False);
            Assert.That(comp.Fuel, Is.EqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnlitCampfireDoesNotCook()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid meat = default;

        await server.WaitAssertion(() =>
        {
            var fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            var user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var plank = server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords);
            Use(server, user, plank, fire); // fed but never lit
            meat = server.EntMan.SpawnEntity("FoodMeat", server.EntMan.GetComponent<TransformComponent>(fire).Coordinates);
        });

        await server.WaitRunTicks(30 * 150);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(meat), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExamineLevelFollowsRemainingTime()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            var user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            Assert.That(comp.FuelLevel, Is.EqualTo(CampfireFuelLevel.Empty));

            // A plank is a minute of burning; the fire holds fifteen.
            var expected = new (int Planks, CampfireFuelLevel Level)[]
            {
                (1, CampfireFuelLevel.Embers), // 1 min
                (3, CampfireFuelLevel.Low), // 3 min
                (5, CampfireFuelLevel.Low), // 5 min
                (7, CampfireFuelLevel.Medium), // 7 min
                (9, CampfireFuelLevel.High), // 9 min
                (11, CampfireFuelLevel.High), // 11 min
                (12, CampfireFuelLevel.Full), // 12 min
                (15, CampfireFuelLevel.Full), // 15 min
            };

            var fed = 0;
            foreach (var (planks, level) in expected)
            {
                for (; fed < planks; fed++)
                {
                    var plank = server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords);
                    Use(server, user, plank, fire);
                }

                Assert.That(comp.FuelLevel, Is.EqualTo(level), $"after {planks} planks (fuel {comp.Fuel})");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LitCampfireWarmsAirUpToCeiling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var atmos = server.System<AtmosphereSystem>();

        EntityUid fire = default;
        EntityUid user = default;

        // A sealed one-tile room: the fire in the middle, walls on all eight neighbours.
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), plating);
                    if (x != 0 || y != 0)
                        server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f));
                }
            }

            server.EntMan.EnsureComponent<GridAtmosphereComponent>(map.Grid);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            fire = server.EntMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            user = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var air = atmos.GetTileMixture(fire);
            Assert.That(air, Is.Not.Null);
            Assert.That(air!.Immutable, Is.False, "test grid should have real air");
            air.Clear();
            air.AdjustMoles(Gas.Oxygen, 21.824779f);
            air.AdjustMoles(Gas.Nitrogen, 82.10312f);
            air.Temperature = Atmospherics.T20C;

            var hot = server.EntMan.SpawnEntity("TestCampfireHotItem", map.GridCoords);
            for (var i = 0; i < 15; i++)
                Use(server, user, server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords), fire);
            Use(server, user, hot, fire);
        });

        await server.WaitRunTicks(30 * 20);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            var air = atmos.GetTileMixture(fire)!;
            Assert.That(comp.Lit, Is.True);
            Assert.That(air.Temperature, Is.GreaterThan(Atmospherics.T20C + 1f));
            Assert.That(air.Temperature, Is.LessThanOrEqualTo(comp.AirHeatMaxTemperature + 1f));
        });

        // Long after the ceiling is reached the tile stays near it instead of running away.
        await server.WaitRunTicks(30 * 400);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            var air = atmos.GetTileMixture(fire)!;
            Assert.That(comp.Lit, Is.True);
            Assert.That(air.Temperature, Is.LessThanOrEqualTo(comp.AirHeatMaxTemperature + 1f));
            Assert.That(air.Temperature, Is.GreaterThan(comp.AirHeatMaxTemperature - 5f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyPlainBonfireUsesCampfireSystem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bonfire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            Assert.That(server.EntMan.HasComponent<CampfireComponent>(bonfire), Is.True);
            Assert.That(server.EntMan.HasComponent<AlwaysHotComponent>(bonfire), Is.False);

            // Other bonfires keep their old always-burning behaviour and cannot cook.
            foreach (var id in new[] { "LegionnaireBonfire", "RMCCampfire" })
            {
                var other = server.EntMan.SpawnEntity(id, map.GridCoords);
                Assert.That(server.EntMan.HasComponent<CampfireComponent>(other), Is.False, id);
                Assert.That(server.EntMan.HasComponent<AlwaysHotComponent>(other), Is.True, id);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            var user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);

            bool Burns(string proto)
            {
                var before = comp.Fuel;
                var item = server.EntMan.SpawnEntity(proto, map.GridCoords);
                Use(server, user, item, fire);
                var gone = server.EntMan.Deleted(item) || server.EntMan.IsQueuedForDeletion(item);
                Assert.That(comp.Fuel > before, Is.EqualTo(gone), $"{proto}: fuel and consumption must agree");
                return gone;
            }

            Assert.That(Burns("MaterialWoodPlank1"), Is.True);
            Assert.That(Burns("Paper"), Is.True, "items with the Paper tag burn");
            Assert.That(Burns("BookSpaceLaw"), Is.True, "books carry the Paper tag");
            Assert.That(Burns("SheetPaper1"), Is.True, "paper material burns");
            Assert.That(Burns("PaperScrap"), Is.False, "anything tagged Trash is not fuel, even paper");
            Assert.That(Burns("TrashBananaPeel"), Is.False, "trash is not fuel");
            Assert.That(Burns("FoodPacketLollypopTrash"), Is.False, "trash is not fuel even when made of paper");
            Assert.That(Burns("Shovel"), Is.False, "shovels are not fuel");
            Assert.That(Burns("MobMouse"), Is.False, "creatures are not fuel");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CampfireGoesOutWithoutOxygen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap(); // space: no air at all

        EntityUid fire = default;

        await server.WaitAssertion(() =>
        {
            fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            var user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            for (var i = 0; i < 5; i++)
                Use(server, user, server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords), fire);
            Use(server, user, server.EntMan.SpawnEntity("TestCampfireHotItem", map.GridCoords), fire);
            Assert.That(server.EntMan.GetComponent<CampfireComponent>(fire).Lit, Is.True);
        });

        // Still burning within the 3 second grace period...
        await server.WaitRunTicks(30 * 2);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<CampfireComponent>(fire).Lit, Is.True));

        // ...and out shortly after, with fuel left over.
        await server.WaitRunTicks(30 * 4);
        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            Assert.That(comp.Lit, Is.False);
            Assert.That(comp.Fuel, Is.GreaterThan(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CampfireKeepsBurningInAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);

        EntityUid fire = default;

        await server.WaitAssertion(() =>
        {
            fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            var user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            for (var i = 0; i < 5; i++)
                Use(server, user, server.EntMan.SpawnEntity("MaterialWoodPlank1", map.GridCoords), fire);
            Use(server, user, server.EntMan.SpawnEntity("TestCampfireHotItem", map.GridCoords), fire);
        });

        await server.WaitRunTicks(30 * 20);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<CampfireComponent>(fire).Lit, Is.True));

        await pair.CleanReturnAsync();
    }

    /// <summary>A lit fire with the given number of planks of fuel, in a room with air.</summary>
    private static (EntityUid Fire, EntityUid User) LightFire(RobustIntegrationTest.ServerIntegrationInstance server,
        EntityCoordinates coords,
        int planks)
    {
        var fire = server.EntMan.SpawnEntity("Bonfire", coords);
        var user = server.EntMan.SpawnEntity("MobHuman", coords);
        for (var i = 0; i < planks; i++)
            Use(server, user, server.EntMan.SpawnEntity("MaterialWoodPlank1", coords), fire);
        Use(server, user, server.EntMan.SpawnEntity("TestCampfireHotItem", coords), fire);
        Assert.That(server.EntMan.GetComponent<CampfireComponent>(fire).Lit, Is.True);
        return (fire, user);
    }

    private static bool IsLit(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid fire)
        => server.EntMan.GetComponent<CampfireComponent>(fire).Lit;

    [Test]
    public async Task StompingDependsOnFireSize()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);
        var campfires = server.System<CampfireSystem>();

        EntityUid small = default, smallUser = default, medium = default, mediumUser = default, large = default, largeUser = default;

        await server.WaitAssertion(() =>
        {
            (small, smallUser) = LightFire(server, map.GridCoords, 2); // 2 minutes: small
            (medium, mediumUser) = LightFire(server, map.GridCoords, 7); // 7 minutes: medium
            (large, largeUser) = LightFire(server, map.GridCoords, 15); // 15 minutes: large

            // The big one just sets the stamper alight.
            campfires.StartStomp((large, server.EntMan.GetComponent<CampfireComponent>(large)), largeUser);
            Assert.That(server.EntMan.GetComponent<FlammableComponent>(largeUser).OnFire, Is.True);
            Assert.That(IsLit(server, large), Is.True);

            campfires.StartStomp((small, server.EntMan.GetComponent<CampfireComponent>(small)), smallUser);
            campfires.StartStomp((medium, server.EntMan.GetComponent<CampfireComponent>(medium)), mediumUser);
            Assert.That(server.EntMan.GetComponent<FlammableComponent>(smallUser).OnFire, Is.False);
        });

        await server.WaitRunTicks(30 * 4);
        await server.WaitAssertion(() =>
        {
            Assert.That(IsLit(server, small), Is.True, "5 seconds are needed for a small fire");
            Assert.That(IsLit(server, medium), Is.True);
        });

        await server.WaitRunTicks(30 * 2); // 6 s in
        await server.WaitAssertion(() =>
        {
            Assert.That(IsLit(server, small), Is.False, "small fire is out after 5 seconds");
            Assert.That(IsLit(server, medium), Is.True, "medium fire needs 10 seconds");
        });

        await server.WaitRunTicks(30 * 5); // 11 s in
        await server.WaitAssertion(() =>
        {
            Assert.That(IsLit(server, medium), Is.False, "medium fire is out after 10 seconds");
            Assert.That(IsLit(server, large), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WaterNeedsFiftyUnits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);
        var solutions = server.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var (fire, user) = LightFire(server, map.GridCoords, 15);
            var bucket = server.EntMan.SpawnEntity("Bucket", map.GridCoords);
            Assert.That(solutions.TryGetDrainableSolution(bucket, out var soln, out var solution), Is.True);

            FixedPoint2 Water() => solution!.GetReagentQuantity(new ReagentId("Water", null));

            solutions.TryAddReagent(soln!.Value, "Water", 49);
            Use(server, user, bucket, fire);
            Assert.That(IsLit(server, fire), Is.True, "49 units are not enough");
            Assert.That(Water(), Is.EqualTo(FixedPoint2.New(49)), "and are not used up");

            solutions.TryAddReagent(soln.Value, "Water", 1);
            Use(server, user, bucket, fire);
            Assert.That(IsLit(server, fire), Is.False, "50 units douse even a large fire");
            Assert.That(Water(), Is.EqualTo(FixedPoint2.Zero), "50 units were used");

            // An empty bucket is no water source: it is just put down.
            var (fire2, user2) = LightFire(server, map.GridCoords, 15);
            Use(server, user2, server.EntMan.SpawnEntity("Bucket", map.GridCoords), fire2);
            Assert.That(IsLit(server, fire2), Is.True);

            // A big splash douses it as well.
            var (fire3, _) = LightFire(server, map.GridCoords, 15);
            server.System<ReactiveSystem>().ReactionEntity(fire3, ReactionMethod.Touch, new ReagentQuantity("Water", 50), null);
            Assert.That(IsLit(server, fire3), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExtinguisherSprays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);
        var reactive = server.System<ReactiveSystem>();

        EntityUid medium = default, large = default;

        void Spray(EntityUid target) =>
            reactive.ReactionEntity(target, ReactionMethod.Touch, new ReagentQuantity("Water", 2), null);

        await server.WaitAssertion(() =>
        {
            (medium, _) = LightFire(server, map.GridCoords, 7);
            (large, _) = LightFire(server, map.GridCoords, 15);

            Spray(medium);
            Assert.That(IsLit(server, medium), Is.False, "one spray puts out a medium fire");

            // Particles of one spray arrive together and count once.
            Spray(large);
            Spray(large);
            Spray(large);
            Assert.That(IsLit(server, large), Is.True);
            Assert.That(server.EntMan.GetComponent<CampfireComponent>(large).SprayHits, Is.EqualTo(1));
        });

        // Two or three separate sprays are needed for a large fire.
        var sprays = 1;
        while (sprays < 3)
        {
            await server.WaitRunTicks(30 * 2);
            await server.WaitAssertion(() =>
            {
                Assert.That(IsLit(server, large), Is.True, $"still burning after {sprays} sprays");
                Spray(large);
            });

            sprays++;
            var out_ = false;
            await server.WaitAssertion(() => out_ = !IsLit(server, large));
            if (out_)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(IsLit(server, large), Is.False, "out after at most three sprays");
            Assert.That(sprays, Is.InRange(2, 3));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealExtinguisherVaporPutsOutFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await AddAir(server, map.MapUid);

        EntityUid fire = default;

        await server.WaitAssertion(() =>
        {
            (fire, _) = LightFire(server, map.GridCoords, 7); // medium fire

            // A puff of extinguisher spray drifting into the fire.
            var coords = server.EntMan.GetComponent<TransformComponent>(fire).Coordinates;
            var vapor = server.EntMan.SpawnEntity("ExtinguisherSpray", coords);
            var solution = new Solution();
            solution.AddReagent("Water", 2);
            Assert.That(server.System<VaporSystem>().TryAddSolution((vapor, server.EntMan.GetComponent<VaporComponent>(vapor)), solution), Is.True);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() => Assert.That(IsLit(server, fire), Is.False, "vapor collision should reach the fire"));

        await pair.CleanReturnAsync();
    }
}
