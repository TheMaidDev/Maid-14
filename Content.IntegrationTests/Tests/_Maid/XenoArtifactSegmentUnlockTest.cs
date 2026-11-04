using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Maid;

[TestFixture]
public sealed class XenoArtifactSegmentUnlockTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestSegmentUnlockArtifact
  parent: BaseXenoArtifact
  components:
  - type: XenoArtifact
    isGenerationRequired: false
    effectsTable: !type:NestedSelector
      tableId: XenoArtifactEffectsDefaultTable

- type: entity
  id: TestSegmentUnlockNode
  components:
  - type: XenoArtifactNode
";

    [TestCase(false, TestName = "IndependentRootsUnlockInOneWindow")]
    [TestCase(true, TestName = "ExtraSiblingTriggerOnlyBlocksItsOwnSegment")]
    public async Task SegmentUnlocking(bool conflictingSibling)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var system = entManager.System<SharedXenoArtifactSystem>();

        await server.WaitPost(() =>
        {
            var artifactUid = entManager.Spawn("TestSegmentUnlockArtifact");
            Entity<XenoArtifactComponent> artifact = (artifactUid, entManager.GetComponent<XenoArtifactComponent>(artifactUid));
            var root = AddNode(system, artifact);
            var first = AddNode(system, artifact);
            var sibling = AddNode(system, artifact);
            var independent = AddNode(system, artifact);
            var independentChild = AddNode(system, artifact);
            system.AddEdge(artifact, root, first);
            system.AddEdge(artifact, root, sibling);
            system.AddEdge(artifact, independent, independentChild);

            if (conflictingSibling)
            {
                system.SetNodeUnlocked(artifact, root);
                system.TriggerXenoArtifact(artifact, first, force: true);
                system.TriggerXenoArtifact(artifact, sibling, force: true);
            }

            system.TriggerXenoArtifact(artifact, root, force: true);
            system.TriggerXenoArtifact(artifact, independent, force: true);
            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(artifactUid);
            system.FinishUnlockingState((artifactUid, unlocking, artifact.Comp));

            Assert.Multiple(() =>
            {
                Assert.That(root.Comp.Locked, Is.False);
                Assert.That(independent.Comp.Locked, Is.False);
                Assert.That(first.Comp.Locked, Is.True);
                Assert.That(sibling.Comp.Locked, Is.True);
                Assert.That(independentChild.Comp.Locked, Is.True);
                Assert.That(entManager.HasComponent<XenoArtifactUnlockingComponent>(artifactUid), Is.False);
            });
            entManager.DeleteEntity(artifactUid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArtifexiumSkipsUntouchedSegmentsWhenAnotherWasTriggered(bool triggerFirstSegment)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var system = entManager.System<SharedXenoArtifactSystem>();
        await server.WaitPost(() =>
        {
            var uid = entManager.Spawn("TestSegmentUnlockArtifact");
            Entity<XenoArtifactComponent> artifact = (uid, entManager.GetComponent<XenoArtifactComponent>(uid));
            var root = AddNode(system, artifact);
            var child = AddNode(system, artifact);
            var untouched = AddNode(system, artifact);
            system.AddEdge(artifact, root, child);
            if (triggerFirstSegment)
                system.SetNodeUnlocked(artifact, root);
            system.TriggerXenoArtifact(artifact, triggerFirstSegment ? root : null, force: true);
            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(uid);
            system.SetArtifexiumApplied((uid, unlocking), true);
            system.FinishUnlockingState((uid, unlocking, artifact.Comp));
            Assert.Multiple(() =>
            {
                Assert.That(root.Comp.Locked, Is.False);
                Assert.That(child.Comp.Locked, Is.EqualTo(!triggerFirstSegment));
                Assert.That(untouched.Comp.Locked, Is.EqualTo(triggerFirstSegment));
            });
            entManager.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    private static Entity<XenoArtifactNodeComponent> AddNode(SharedXenoArtifactSystem system, Entity<XenoArtifactComponent> artifact)
    {
        Assert.That(system.AddNode(artifact, "TestSegmentUnlockNode", out var node), Is.True);
        return node!.Value;
    }
}
