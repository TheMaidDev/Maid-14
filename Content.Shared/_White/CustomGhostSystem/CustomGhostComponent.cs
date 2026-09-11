// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._White.CustomGhostSystem;
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CustomGhostComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<CustomGhostPrototype>? Ghost;
}
