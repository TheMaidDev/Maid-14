using System.Linq;
using Content.Shared._Maid.Campfire;
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
}
