// SPDX-FileCopyrightText: 2023 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Emisse <99158783+Emisse@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Stacks; // MAID revert machine parts
using Robust.Shared.GameObjects; // MAID revert machine parts

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class MachineConstruction : InteractionTest
{
    private const string MachineFrame = "MachineFrame";
    private const string Unfinished = "UnfinishedMachineFrame";
    private const string ProtolatheBoard = "ProtolatheMachineCircuitboard";
    private const string Protolathe = "Protolathe";
    private const string Beaker = "Beaker";

    [Test]
    public async Task ConstructProtolathe()
    {
        await StartConstruction(MachineFrame);
        await InteractUsing(Steel, 5);
        ClientAssertPrototype(Unfinished, Target);
        await Interact(Wrench, Cable);
        AssertPrototype(MachineFrame);
        await Interact(ProtolatheBoard, Bin1, Bin1, Manipulator1, Manipulator1, Beaker, Beaker, Screw); // MAID add Bin1, Bin1
        AssertPrototype(Protolathe);
    }

    [Test]
    public async Task DeconstructProtolathe()
    {
        await StartDeconstruction(Protolathe);
        await Interact(Screw, Pry);
        AssertPrototype(MachineFrame);
        await Interact(Pry, Cut);
        AssertPrototype(Unfinished);
        await Interact(Wrench, Screw);
        AssertDeleted();
        await AssertEntityLookup(
            (Steel, 5),
            (Cable, 1),
            (Beaker, 2),
            (Manipulator1, 2), // MAID (Manipulator1, 4) -> (Manipulator1, 2)
            (Bin1, 2), // MAID revert machine parts
            (ProtolatheBoard, 1));
    }

    [Test]
    public async Task ChangeMachine()
    {
        // Partially deconstruct a protolathe.
        await SpawnTarget(Protolathe);
        await Interact(Screw, Pry, Pry);
        AssertPrototype(MachineFrame);

        // Change it into an autolathe
        await InteractUsing("AutolatheMachineCircuitboard");
        AssertPrototype(MachineFrame);
        await Interact(Bin1, Bin1, Bin1, Manipulator1, Glass, Screw); // MAID Manipulator1, Manipulator1, Manipulator1 -> Bin1, Bin1, Bin1
        AssertPrototype("Autolathe");
    }

    // MAID BEGIN revert machine parts
    /*
    [Test]
    public async Task UpgradeLathe()
    {
        // Partially deconstruct a protolathe.
        await SpawnTarget(Protolathe);
        var serverTarget = SEntMan.GetEntity(Target!.Value);

        // Initially has all quality-1 parts.
        foreach (var part in SConstruction.GetAllParts(serverTarget))
        {
            Assert.That(part.Rating, Is.EqualTo(1));
        }

        // Partially deconstruct lathe
        await Interact(Screw, Pry, Pry);
        AssertPrototype(MachineFrame);

        // Reconstruct with better parts.
        await Interact(ProtolatheBoard, Bin4, Bin4, Manipulator4, Manipulator4, Beaker, Beaker);
        await Interact(Screw);
        AssertPrototype(Protolathe);

        // Query now returns higher quality parts.
        foreach (var part in SConstruction.GetAllParts(SEntMan.GetEntity(Target!.Value)))
        {
            Assert.That(part.Rating, Is.EqualTo(4));
        }
    }
    */

    /// <summary>
    /// Machine upgrading (part ratings) was ripped out upstream in #24413, so we can only check
    /// that higher tier parts are still accepted by machine frames as their base stack type.
    /// </summary>
    [Test]
    public async Task RebuildLatheWithHighTierParts()
    {
        // Partially deconstruct a protolathe.
        await SpawnTarget(Protolathe);

        // Partially deconstruct lathe
        await Interact(Screw, Pry, Pry);
        AssertPrototype(MachineFrame);

        // Reconstruct with better parts.
        await Interact(ProtolatheBoard, Bin4, Bin4, Manipulator4, Manipulator4, Beaker, Beaker);
        await Interact(Screw);
        AssertPrototype(Protolathe);

        // Deconstruct it again, the inserted parts must come back with the same rating
        // (they share a stack type with basic parts, splitting them used to spawn basic ones).
        await Interact(Screw, Pry, Pry);
        AssertPrototype(MachineFrame);

        var entities = await DoEntityLookup();
        await Server.WaitAssertion(() =>
        {
            int Count(string proto)
            {
                var count = 0;
                foreach (var ent in entities)
                {
                    if (SEntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID != proto)
                        continue;

                    count += SEntMan.TryGetComponent<StackComponent>(ent, out var stack) ? stack.Count : 1;
                }

                return count;
            }

            Assert.That(Count(Bin4), Is.EqualTo(2));
            Assert.That(Count(Manipulator4), Is.EqualTo(2));
        });
    }
    // MAID END revert machine parts
}
