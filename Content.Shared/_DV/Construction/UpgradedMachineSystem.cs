// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 deltanedas <39013340+deltanedas@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Construction; // MAID return upgrade kit
using Content.Shared.Examine;
using Robust.Shared.Network; // MAID return upgrade kit

namespace Content.Shared._DV.Construction;

public sealed class UpgradedMachineSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!; // MAID return upgrade kit

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UpgradedMachineComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<UpgradedMachineComponent, MachineDeconstructedEvent>(OnDeconstructed); // MAID return upgrade kit
    }

    private void OnExamined(Entity<UpgradedMachineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.Upgrade));
    }

    // MAID BEGIN return upgrade kit
    private void OnDeconstructed(EntityUid uid, UpgradedMachineComponent comp, MachineDeconstructedEvent args)
    {
        if (_net.IsClient || comp.Kit is not {} kit)
            return;

        Spawn(kit, Transform(uid).Coordinates);
    }
    // MAID END return upgrade kit
}
