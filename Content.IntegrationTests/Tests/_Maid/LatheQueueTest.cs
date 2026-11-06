using System.Linq;
using Content.Goobstation.Shared.Lathe;
using Content.Server.Lathe;
using Content.Server.Lathe.Components;
using Content.Server.Power.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Maid;

[TestFixture]
public sealed class LatheQueueTest
{
    [Test]
    public async Task QueueIsLimitedToTwenty()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var lathe = server.System<LatheSystem>();
        var storage = server.System<SharedMaterialStorageSystem>();
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity("Autolathe", map.GridCoords);
            var comp = server.EntMan.GetComponent<LatheComponent>(uid);
            var recipe = protoMan.Index<LatheRecipePrototype>("Welder");

            foreach (var mat in recipe.Materials.Keys)
                storage.TryChangeMaterialAmount(uid, mat, 1_000_000);

            var before = recipe.Materials.ToDictionary(m => m.Key, m => storage.GetMaterialAmount(uid, m.Key));

            var added = 0;
            for (var i = 0; i < 30; i++)
            {
                if (lathe.TryAddToQueue(uid, recipe, comp))
                    added++;
            }

            Assert.That(added, Is.EqualTo(SharedLatheSystem.MaxQueueSize));
            Assert.That(comp.Queue.Count, Is.EqualTo(20));

            // Rejected items must not consume materials.
            foreach (var (mat, amount) in recipe.Materials)
            {
                var spent = before[mat] - storage.GetMaterialAmount(uid, mat);
                Assert.That(spent, Is.GreaterThan(0));
                Assert.That(spent, Is.LessThanOrEqualTo(amount * 20));
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllRecipesTakeAtLeastOneSecond()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ProtoMan;

        Assert.Multiple(() =>
        {
            foreach (var recipe in protoMan.EnumeratePrototypes<LatheRecipePrototype>())
            {
                Assert.That(recipe.CompleteTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)), recipe.ID);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BatchIsOneSlotAndPrintsRepeatedly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var lathe = server.System<LatheSystem>();
        var storage = server.System<SharedMaterialStorageSystem>();

        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = server.EntMan.SpawnEntity("Autolathe", map.GridCoords);
            server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).NeedsPower = false;
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<LatheComponent>(uid);
            var recipe = server.ProtoMan.Index<LatheRecipePrototype>("Welder");
            foreach (var mat in recipe.Materials.Keys)
                storage.TryChangeMaterialAmount(uid, mat, 1_000_000);

            var before = recipe.Materials.ToDictionary(m => m.Key, m => storage.GetMaterialAmount(uid, m.Key));

            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 51), Is.False, "over max batch");
            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 0), Is.False);
            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 10), Is.True);
            Assert.That(comp.Queue.Count, Is.EqualTo(1));
            foreach (var (mat, amount) in recipe.Materials)
                Assert.That(before[mat] - storage.GetMaterialAmount(uid, mat), Is.EqualTo(amount * 10));

            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 50), Is.True);
            Assert.That(comp.Queue.Count, Is.EqualTo(2));

            Assert.That(lathe.TryStartProducing(uid, comp), Is.True);
            Assert.That(comp.Queue.Count, Is.EqualTo(1));
            var printed = 0;
            while (comp.CurrentRecipe != null && comp.BatchTotal == 10)
            {
                printed++;
                lathe.FinishProducing(uid, comp);
            }

            // 10 from the first batch, then the second batch started.
            Assert.That(printed, Is.EqualTo(10));
            Assert.That(comp.BatchTotal, Is.EqualTo(50));
            Assert.That(comp.BatchRemaining, Is.EqualTo(49));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResetRefundsWholeBatches()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var lathe = server.System<LatheSystem>();
        var storage = server.System<SharedMaterialStorageSystem>();

        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = server.EntMan.SpawnEntity("Autolathe", map.GridCoords);
            server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).NeedsPower = false;
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<LatheComponent>(uid);
            var recipe = server.ProtoMan.Index<LatheRecipePrototype>("Welder");
            foreach (var mat in recipe.Materials.Keys)
                storage.TryChangeMaterialAmount(uid, mat, 1_000_000);

            var start = recipe.Materials.ToDictionary(m => m.Key, m => storage.GetMaterialAmount(uid, m.Key));

            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 10));
            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 5));
            Assert.That(lathe.TryStartProducing(uid, comp));

            server.EntMan.EventBus.RaiseLocalEvent(uid, new LatheQueueResetMessage());

            Assert.That(comp.Queue.Count, Is.EqualTo(0));
            Assert.That(comp.BatchRemaining, Is.EqualTo(0));
            // Only the unit currently being printed stays spent.
            foreach (var (mat, amount) in recipe.Materials)
                Assert.That(start[mat] - storage.GetMaterialAmount(uid, mat), Is.EqualTo(amount));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelEntryRefundsAndKeepsOthers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var lathe = server.System<LatheSystem>();
        var storage = server.System<SharedMaterialStorageSystem>();

        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            uid = server.EntMan.SpawnEntity("Autolathe", map.GridCoords);
            server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).NeedsPower = false;
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<LatheComponent>(uid);
            var welder = server.ProtoMan.Index<LatheRecipePrototype>("Welder");
            foreach (var mat in welder.Materials.Keys)
                storage.TryChangeMaterialAmount(uid, mat, 1_000_000);
            var start = welder.Materials.ToDictionary(m => m.Key, m => storage.GetMaterialAmount(uid, m.Key));
            var bus = server.EntMan.EventBus;

            Assert.That(lathe.TryAddToQueue(uid, welder, comp, 4)); // running
            Assert.That(lathe.TryAddToQueue(uid, welder, comp, 3)); // queue 0
            Assert.That(lathe.TryAddToQueue(uid, welder, comp, 2)); // queue 1
            Assert.That(lathe.TryStartProducing(uid, comp));
            Assert.That(comp.Queue.Count, Is.EqualTo(2));

            // Cancel the first queued entry: 3 units back, the other stays.
            bus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(0, welder.ID, 3));
            Assert.That(comp.Queue.Count, Is.EqualTo(1));
            Assert.That(comp.Queue.Peek().Amount, Is.EqualTo(2));
            foreach (var (mat, amount) in welder.Materials)
                Assert.That(start[mat] - storage.GetMaterialAmount(uid, mat), Is.EqualTo(amount * 6));

            // Out of range index is ignored.
            bus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(7, welder.ID, 2));
            Assert.That(comp.Queue.Count, Is.EqualTo(1));

            // A stale view (entry changed since the client looked) must not cancel anything.
            bus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(0, welder.ID, 3));
            Assert.That(comp.Queue.Count, Is.EqualTo(1));
            bus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(LatheCancelQueueEntryMessage.Current, "Crowbar"));
            Assert.That(comp.CurrentRecipe, Is.Not.Null);
            foreach (var (mat, amount) in welder.Materials)
                Assert.That(start[mat] - storage.GetMaterialAmount(uid, mat), Is.EqualTo(amount * 6));

            // Cancel the running batch: its 4 units come back and the next entry starts.
            bus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(LatheCancelQueueEntryMessage.Current, welder.ID));
            Assert.That(comp.Queue.Count, Is.EqualTo(0));
            Assert.That(comp.CurrentRecipe, Is.Not.Null);
            Assert.That(comp.BatchTotal, Is.EqualTo(2));
            foreach (var (mat, amount) in welder.Materials)
                Assert.That(start[mat] - storage.GetMaterialAmount(uid, mat), Is.EqualTo(amount * 2));
            Assert.That(server.EntMan.HasComponent<LatheProducingComponent>(uid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueueAndCancelCannotCreateMaterial()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var lathe = server.System<LatheSystem>();
        var storage = server.System<SharedMaterialStorageSystem>();

        EntityUid uid = default;
        await server.WaitPost(() => uid = server.EntMan.SpawnEntity("Autolathe", map.GridCoords));

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<LatheComponent>(uid);
            comp.MaterialUseMultiplier = 0.75f;
            var recipe = server.ProtoMan.Index<LatheRecipePrototype>("Welder");
            foreach (var mat in recipe.Materials.Keys)
                storage.TryChangeMaterialAmount(uid, mat, 1_000_000);
            var start = recipe.Materials.ToDictionary(m => m.Key, m => storage.GetMaterialAmount(uid, m.Key));

            Assert.That(lathe.TryAddToQueue(uid, recipe, comp, 7));
            server.EntMan.EventBus.RaiseLocalEvent(uid, new LatheCancelQueueEntryMessage(0, recipe.ID, 7));

            foreach (var mat in recipe.Materials.Keys)
                Assert.That(storage.GetMaterialAmount(uid, mat), Is.EqualTo(start[mat]));
        });

        await pair.CleanReturnAsync();
    }
}
