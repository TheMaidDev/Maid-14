// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 deltanedas <39013340+deltanedas@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes; // MAID return upgrade kit

namespace Content.Shared._DV.Construction;

/// <summary>
/// Component added to machines to prevent stacking upgrades and show what upgrade they have.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(UpgradedMachineSystem))]
[AutoGenerateComponentState]
public sealed partial class UpgradedMachineComponent : Component
{
    /// <summary>
    /// The string to show when examined.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public LocId Upgrade;

    // MAID BEGIN return upgrade kit
    /// <summary>
    /// Upgrade kit spawned when the machine is deconstructed.
    /// Leave null for machines that come pre-upgraded.
    /// </summary>
    [DataField]
    public EntProtoId? Kit;
    // MAID END return upgrade kit
}
