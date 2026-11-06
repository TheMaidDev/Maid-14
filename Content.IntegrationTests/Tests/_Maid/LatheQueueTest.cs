using System.Linq;
using Content.Server.Lathe;
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

            Assert.That(added, Is.EqualTo(LatheSystem.MaxQueueSize));
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
}
