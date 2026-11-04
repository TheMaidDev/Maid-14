using System.Linq;
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task IndependentRootsUnlockInOneWindow(bool reverseOrder)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var system = entManager.System<SharedXenoArtifactSystem>();

        await server.WaitPost(() =>
        {
            var artifactUid = entManager.Spawn("TestSegmentUnlockArtifact");
            Entity<XenoArtifactComponent> artifact = (artifactUid, entManager.GetComponent<XenoArtifactComponent>(artifactUid));
            var roots = new[] { AddNode(system, artifact), AddNode(system, artifact) };
            var children = roots.Select(root =>
            {
                var child = AddNode(system, artifact);
                Assert.That(system.AddEdge(artifact, root, child), Is.True);
                return child;
            }).ToArray();

            foreach (var root in reverseOrder ? roots.Reverse() : roots)
                system.TriggerXenoArtifact(artifact, root, force: true);

            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(artifactUid);
            Assert.That(system.GetNodesFromUnlockState((artifactUid, unlocking, artifact.Comp)).Select(node => node.Owner),
                Is.EquivalentTo(roots.Select(node => node.Owner)));

            system.FinishUnlockingState((artifactUid, unlocking, artifact.Comp));
            Assert.That(roots.All(node => !node.Comp.Locked), Is.True);
            Assert.That(children.All(node => node.Comp.Locked), Is.True, "Only one node per segment may open per window.");
            Assert.That(entManager.HasComponent<XenoArtifactUnlockingComponent>(artifactUid), Is.False);
            entManager.DeleteEntity(artifactUid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidSegmentDoesNotBlockIndependentRoot(bool missingPredecessor)
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
            var second = AddNode(system, artifact);
            var independent = AddNode(system, artifact);
            system.AddEdge(artifact, root, first);
            system.AddEdge(artifact, root, second);

            if (!missingPredecessor)
            {
                system.SetNodeUnlocked(artifact, root);
                system.TriggerXenoArtifact(artifact, root, force: true);
                system.TriggerXenoArtifact(artifact, second, force: true);
            }

            system.TriggerXenoArtifact(artifact, first, force: true);
            system.TriggerXenoArtifact(artifact, independent, force: true);
            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(artifactUid);
            Assert.That(system.GetNodesFromUnlockState((artifactUid, unlocking, artifact.Comp)).Select(node => node.Owner),
                Is.EquivalentTo(new[] { independent.Owner }));

            system.FinishUnlockingState((artifactUid, unlocking, artifact.Comp));
            Assert.That(independent.Comp.Locked, Is.False);
            Assert.That(first.Comp.Locked && second.Comp.Locked, Is.True);
            entManager.DeleteEntity(artifactUid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArtifexiumChoosesOneCandidatePerSegment(bool triggerParents)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var system = entManager.System<SharedXenoArtifactSystem>();

        await server.WaitPost(() =>
        {
            var artifactUid = entManager.Spawn("TestSegmentUnlockArtifact");
            Entity<XenoArtifactComponent> artifact = (artifactUid, entManager.GetComponent<XenoArtifactComponent>(artifactUid));
            var roots = new[] { AddNode(system, artifact), AddNode(system, artifact) };
            var segments = roots.Select(root =>
            {
                var children = new[] { AddNode(system, artifact), AddNode(system, artifact) };
                foreach (var child in children)
                    system.AddEdge(artifact, root, child);
                return children;
            }).ToArray();

            if (triggerParents)
            {
                foreach (var root in roots)
                {
                    system.SetNodeUnlocked(artifact, root);
                    system.TriggerXenoArtifact(artifact, root, force: true);
                }
            }
            else
                system.TriggerXenoArtifact(artifact, null, force: true);

            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(artifactUid);
            system.SetArtifexiumApplied((artifactUid, unlocking), true);
            var nodes = system.GetNodesFromUnlockState((artifactUid, unlocking, artifact.Comp));
            Assert.That(nodes, Has.Count.EqualTo(2));
            if (triggerParents)
            {
                foreach (var segment in segments)
                    Assert.That(nodes.Count(node => segment.Any(child => child.Owner == node.Owner)), Is.EqualTo(1));
            }
            else
                Assert.That(nodes.Select(node => node.Owner), Is.EquivalentTo(roots.Select(node => node.Owner)));

            system.FinishUnlockingState((artifactUid, unlocking, artifact.Comp));
            foreach (var segment in segments)
                Assert.That(segment.Count(child => !child.Comp.Locked), Is.EqualTo(triggerParents ? 1 : 0));
            entManager.DeleteEntity(artifactUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WindowExtensionIgnoresOtherSegmentsButRejectsSiblingTriggers()
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
            system.AddEdge(artifact, root, first);
            system.AddEdge(artifact, root, sibling);
            system.SetNodeUnlocked(artifact, root);
            system.TriggerXenoArtifact(artifact, root, force: true);
            system.TriggerXenoArtifact(artifact, independent, force: true);
            var unlocking = entManager.GetComponent<XenoArtifactUnlockingComponent>(artifactUid);
            var previousEnd = unlocking.EndTime;
            system.TriggerXenoArtifact(artifact, first, force: true);
            Assert.That(unlocking.EndTime, Is.EqualTo(previousEnd + artifact.Comp.UnlockStateIncrementPerNode));
            previousEnd = unlocking.EndTime;
            system.TriggerXenoArtifact(artifact, sibling, force: true);
            Assert.That(unlocking.EndTime, Is.EqualTo(previousEnd));
            system.CancelUnlockingState((artifactUid, unlocking, artifact.Comp));
            Assert.That(first.Comp.Locked && sibling.Comp.Locked && independent.Comp.Locked, Is.True);
            entManager.DeleteEntity(artifactUid);
        });

        await pair.CleanReturnAsync();
    }

    private static Entity<XenoArtifactNodeComponent> AddNode(SharedXenoArtifactSystem system, Entity<XenoArtifactComponent> artifact)
    {
        Assert.That(system.AddNode(artifact, "TestSegmentUnlockNode", out var node), Is.True);
        return node!.Value;
    }
}
