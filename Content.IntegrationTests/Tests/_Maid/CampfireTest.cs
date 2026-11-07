using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Maid.Campfire;
using Content.Shared.Atmos;
using Content.Shared.Temperature.Components;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

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

        await server.WaitPost(() =>
        {
            server.EntMan.AddComponent<GridAtmosphereComponent>(map.Grid);
            server.EntMan.System<AtmosphereSystem>();
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            fire = server.EntMan.SpawnEntity("Bonfire", map.GridCoords);
            user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
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
            // Atmos may have spread some heat to neighbours, never past the ceiling.
            Assert.That(air.Temperature, Is.LessThanOrEqualTo(comp.AirHeatMaxTemperature + 10f));
        });

        // Long after the ceiling is reached the tile stays near it instead of running away.
        await server.WaitRunTicks(30 * 400);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<CampfireComponent>(fire);
            var air = atmos.GetTileMixture(fire)!;
            Assert.That(air.Temperature, Is.LessThanOrEqualTo(comp.AirHeatMaxTemperature + 10f));
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
}
