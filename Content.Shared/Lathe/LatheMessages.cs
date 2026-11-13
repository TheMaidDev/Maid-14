// SPDX-FileCopyrightText: 2019 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2019 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2019 Víctor Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2019 ZelteHonor <gabrieldionbouchard@gmail.com>
// SPDX-FileCopyrightText: 2020 FL-OZ <yetanotherscuffed@gmail.com>
// SPDX-FileCopyrightText: 2021 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2021 Metal Gear Sloth <metalgearsloth@gmail.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <gradientvera@outlook.com>
// SPDX-FileCopyrightText: 2021 Visne <39844191+Visne@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Rane <60792108+Elijahrane@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 ShadowCommander <10494922+ShadowCommander@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Soup-Byte07 <soupbyte30@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Research.Prototypes;
using NetSerializer;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed class LatheUpdateState : BoundUserInterfaceState
{
    public List<ProtoId<LatheRecipePrototype>> Recipes;

    // MAID BEGIN lathe batch queue
    /*
    public ProtoId<LatheRecipePrototype>[] Queue;
    */
    public LatheQueueEntry[] Queue;
    // MAID END lathe batch queue

    public ProtoId<LatheRecipePrototype>? CurrentlyProducing;

    // MAID BEGIN lathe batch queue
    /// <summary>
    /// Units of the current batch still to be printed, including the one in progress.
    /// </summary>
    public int BatchRemaining;

    /// <summary>
    /// Total size of the current batch.
    /// </summary>
    public int BatchTotal;

    /// <summary>
    /// Id of the batch shown as currently producing.
    /// </summary>
    public int BatchId;
    // MAID END lathe batch queue

    public LatheUpdateState(List<ProtoId<LatheRecipePrototype>> recipes, LatheQueueEntry[] queue, ProtoId<LatheRecipePrototype>? currentlyProducing = null, int batchRemaining = 0, int batchTotal = 0, int batchId = 0) // MAID lathe batch queue
    {
        Recipes = recipes;
        Queue = queue;
        CurrentlyProducing = currentlyProducing;
        // MAID BEGIN lathe batch queue
        BatchRemaining = batchRemaining;
        BatchTotal = batchTotal;
        BatchId = batchId;
        // MAID END lathe batch queue
    }
}

// MAID BEGIN lathe cancel entry
/// <summary>
///     Sent to the server to cancel one batch, queued or currently printing, addressed by its stable id.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheCancelQueueEntryMessage : BoundUserInterfaceMessage
{
    public readonly int BatchId;

    public LatheCancelQueueEntryMessage(int batchId)
    {
        BatchId = batchId;
    }
}
// MAID END lathe cancel entry

// MAID BEGIN lathe batch queue
/// <summary>
/// One queue slot: a recipe printed <see cref="Amount"/> times in a row.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public partial record struct LatheQueueEntry
{
    [DataField]
    public ProtoId<LatheRecipePrototype> Recipe;

    [DataField]
    public int Amount;

    /// <summary>
    /// Unique (per lathe) batch id, used to address this entry from the UI.
    /// </summary>
    [DataField]
    public int Id;

    public LatheQueueEntry(ProtoId<LatheRecipePrototype> recipe, int amount, int id)
    {
        Recipe = recipe;
        Amount = amount;
        Id = id;
    }
}
// MAID END lathe batch queue

/// <summary>
///     Sent to the server to sync material storage and the recipe queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSyncRequestMessage : BoundUserInterfaceMessage
{

}

/// <summary>
///     Sent to the server when a client queues a new recipe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheQueueRecipeMessage : BoundUserInterfaceMessage
{
    public readonly string ID;
    public readonly int Quantity;
    public LatheQueueRecipeMessage(string id, int quantity)
    {
        ID = id;
        Quantity = quantity;
    }
}


[NetSerializable, Serializable]
public enum LatheUiKey
{
    Key,
}
