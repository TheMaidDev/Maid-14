// SPDX-FileCopyrightText: 2021 Paul Ritter <ritter.paul1@googlemail.com>
// SPDX-FileCopyrightText: 2021 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2022 20kdc <asdd2808@gmail.com>
// SPDX-FileCopyrightText: 2022 E F R <602406+Efruit@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <Jezithyr.@gmail.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <Jezithyr@gmail.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <jmaster9999@gmail.com>
// SPDX-FileCopyrightText: 2022 Moony <moonheart08@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Paul <ritter.paul1@googlemail.com>
// SPDX-FileCopyrightText: 2022 Visne <39844191+Visne@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 keronshb <54602815+keronshb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 wrexbe <wrexbe@protonmail.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 dffdff2423 <57052305+dffdff2423@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2025 Winkarst <74284083+Winkarst-cpu@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using Content.Shared._Maid.CVars;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client.Administration.Systems
{
    [UsedImplicitly]
    public sealed class BwoinkSystem : SharedBwoinkSystem
    {
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IConfigurationManager _config = default!;

        public event EventHandler<BwoinkTextMessage>? OnBwoinkTextMessageRecieved;
        private (TimeSpan Timestamp, bool Typing) _lastTypingUpdateSent;
        private TimeSpan _rateLimitExpires;
        private int _rateLimitCount;
        private bool _rateLimitAnnounced;

        protected override void OnBwoinkTextMessage(BwoinkTextMessage message, EntitySessionEventArgs eventArgs)
        {
            OnBwoinkTextMessageRecieved?.Invoke(this, message);
        }

        public bool Send(NetUserId channelId, string text, bool playSound, bool adminOnly)
        {
            var maxLength = _config.GetCVar(MaidCVars.AhelpMaxMessageLength);
            if (text.Length > maxLength)
            {
                SendLocalSystemMessage(channelId,
                    Loc.GetString("bwoink-system-message-too-long", ("limit", maxLength)));

                SendInputTextUpdated(channelId, false, force: true);
                return false;
            }

            var now = _timing.RealTime;
            if (_rateLimitExpires < now)
            {
                _rateLimitExpires = now + TimeSpan.FromSeconds(_config.GetCVar(CCVars.AhelpRateLimitPeriod));
                _rateLimitCount = 0;
                _rateLimitAnnounced = false;
            }

            if (++_rateLimitCount > _config.GetCVar(CCVars.AhelpRateLimitCount))
            {
                if (!_rateLimitAnnounced)
                {
                    SendLocalSystemMessage(channelId, Loc.GetString("bwoink-system-rate-limited"));
                    _rateLimitAnnounced = true;
                }

                SendInputTextUpdated(channelId, false, force: true);
                return false;
            }

            // Reuse the channel ID as the 'true sender'.
            // Server will ignore this and if someone makes it not ignore this (which is bad, allows impersonation!!!), that will help.
            RaiseNetworkEvent(new BwoinkTextMessage(channelId, channelId, text, playSound: playSound, adminOnly: adminOnly));
            SendInputTextUpdated(channelId, false);
            return true;
        }

        private void SendLocalSystemMessage(NetUserId channelId, string text)
        {
            OnBwoinkTextMessageRecieved?.Invoke(this,
                new BwoinkTextMessage(channelId, SystemUserId, text, playSound: false));
        }

        public void SendInputTextUpdated(NetUserId channel, bool typing, bool force = false)
        {
            if (!force &&
                _lastTypingUpdateSent.Typing == typing &&
                _lastTypingUpdateSent.Timestamp + TimeSpan.FromSeconds(1) > _timing.RealTime)
            {
                return;
            }

            _lastTypingUpdateSent = (_timing.RealTime, typing);
            RaiseNetworkEvent(new BwoinkClientTypingUpdated(channel, typing));
        }
    }
}