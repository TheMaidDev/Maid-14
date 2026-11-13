// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 SX-7 <92227810+SX-7@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 SomethingUnbearable <mewatcher102@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;
using Content.Shared.DeviceLinking;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Goobstation.Server.MiscSignaller
{
    [RegisterComponent]
    public sealed partial class MiscSignallerComponent : Component
    {
        [DataField]
        public ProtoId<SourcePortPrototype> Port = "Triggered";
       
        [DataField]
        public TimeSpan ActivationInterval = TimeSpan.FromSeconds(3);
       
        public TimeSpan NextActivationWindow;
    }
}
