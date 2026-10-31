// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gluesniffler <linebarrelerenthusiast@gmail.com>
// SPDX-FileCopyrightText: 2025 vanx <61917534+Vaaankas@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Server.IdentityManagement;
using Content.Goobstation.Common.Examine; // Goobstation Change
using Content.Shared._Goobstation.Heretic.Components; // Goobstation Change
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using System.Globalization;
using Content.Shared._Maid.CVars;
using Content.Shared._Maid.UserInterface;
using Content.Shared.IdentityManagement.Components;

namespace Content.Server._White.Examine;
public sealed class ExaminableCharacterSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly IdentitySystem _identitySystem = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly INetConfigurationManager _netConfigManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExaminableCharacterComponent, ExaminedEvent>(HandleExamine);
        SubscribeLocalEvent<MetaDataComponent, ExamineCompletedEvent>(HandleExamine);
    }

    private void HandleExamine(EntityUid uid, ExaminableCharacterComponent comp, ExaminedEvent args)
    {
        if (!TryComp<ActorComponent>(args.Examiner, out var actorComponent)
            || !args.IsInDetailsRange)
            return;

        var examineType = _netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, MaidCVars.DetailedExamineStyle);

        var selfaware = args.Examiner == args.Examined;
        var logLines = new List<string>();

        string canseeloc = "examine-can-see";
        string nameloc = "examine-name";

        if (selfaware)
        {
            canseeloc += "-selfaware";
            nameloc += "-selfaware";
        }
        var identity = _identitySystem.GetEntityIdentity(uid);
        var name = Loc.GetString(nameloc, ("name", identity));
        var formatedName = $"[color=DarkGray][font size=11]{name}[/font][/color]";
        var cansee = Loc.GetString(canseeloc, ("ent", uid));
        logLines.Add($"[color=DarkGray][font size=10]{cansee}[/font][/color]");

        var slotLabels = new Dictionary<string, string>
        {
            { "head", "head-" },
            { "eyes", "eyes-" },
            { "mask", "mask-" },
            { "neck", "neck-" },
            { "ears", "ears-" },
            { "jumpsuit", "jumpsuit-" },
            { "outerClothing", "outer-" },
            { "back", "back-" },
            { "gloves", "gloves-" },
            { "belt", "belt-" },
            { "id", "id-" },
            { "shoes", "shoes-" },
            { "suitstorage", "suitstorage-" }
        };

        var priority = 13;

        foreach (var slotEntry in slotLabels)
        {
            var slotName = slotEntry.Key;
            var slotLabel = slotEntry.Value;

            slotLabel += "examine";

            if (selfaware)
                slotLabel += "-selfaware";

            if (!_inventorySystem.TryGetSlotEntity(uid, slotName, out var slotEntity))
                continue;

            if (_entityManager.TryGetComponent<MetaDataComponent>(slotEntity, out var metaData)
                && !HasComp<StripMenuInvisibleComponent>(slotEntity))
            {
                var itemTex = Loc.GetString(slotLabel, ("item", metaData.EntityName), ("ent", uid), ("id", GetNetEntity(slotEntity.Value).Id), ("size", 14));
                args.PushMarkup($"[font size=10]{Loc.GetString(slotLabel, ("item", metaData.EntityName), ("ent", uid), ("id", "empty"))}[/font]", priority);
                logLines.Add($"[color=DarkGray][font size=10]{itemTex}[/font][/color]");
                priority--;
            }
        }

        if (priority < 13) // If nothing is worn dont show
        {
            args.PushMarkup($"[font size=10]{cansee}[/font]", 14);
        }
        else
        {
            var canseenothingloc = "examine-can-see-nothing";

            if (selfaware)
                canseenothingloc += "-selfaware";

            var canseenothing = Loc.GetString(canseenothingloc, ("ent", uid));
            logLines.Add($"[color=DarkGray][font size=10]{canseenothing}[/font][/color]");
        }

        if (examineType != (int)DetailedExamineType.None)
        {
            if (examineType == (int)DetailedExamineType.Fancy)
            {
                FormattedMessage message = new();
                message.PushTag(new MarkupNode("fancyborder", null, null));
                message.PushNewline();
                message.AddMarkupPermissive(formatedName); // MAID rt-update AddText escapes markup in the new engine
                message.PushNewline();
                foreach (var line in logLines)
                {
                    message.AddMarkupPermissive(line); // MAID rt-update
                    message.PushNewline();
                }
                message.Pop();

                _chatManager.ChatMessageToOne(ChatChannel.Emotes, message.ToString(), message.ToMarkup(), EntityUid.Invalid, false, actorComponent.PlayerSession.Channel, recordReplay: false);
            }
            else
            {
                var combinedLog = $"{formatedName}\n{string.Join($"\n", logLines)}";
                _chatManager.ChatMessageToOne(ChatChannel.Emotes, combinedLog, combinedLog, EntityUid.Invalid, false, actorComponent.PlayerSession.Channel, recordReplay: false);
            }
        }
    }

    private void HandleExamine(EntityUid uid, MetaDataComponent metaData, ExamineCompletedEvent args)
    {
        if (HasComp<ExaminableCharacterComponent>(args.Examined)
            && !args.IsSecondaryInfo)
            return;

        if (!TryComp<ActorComponent>(args.Examiner, out var actorComponent))
            return;

        var displayName = metaData.EntityName;

        if (HasComp<IdentityComponent>(uid))
        {
            displayName = _identitySystem.GetEntityIdentity(uid);
        }

        var examineType = _netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, MaidCVars.DetailedExamineStyle);
        if (examineType != (int)DetailedExamineType.None)
        {
            if (examineType == (int)DetailedExamineType.Fancy)
            {
                FormattedMessage message = new();
                message.PushTag(new MarkupNode("fancyborder", null, null));
                message.PushNewline();

                var textInfo = new CultureInfo("en-US", false).TextInfo;

                var item = Loc.GetString(
                    "examine-present",
                    ("name", args.IsSecondaryInfo ? displayName : textInfo.ToTitleCase(displayName)),
                    ("id", GetNetEntity(uid).Id),
                    ("size", 14));

                message.AddMarkupPermissive($"[color=DarkGray][font size=11]{item}[/font][/color]"); // MAID rt-update
                message.PushNewline();

                message.AddMarkupPermissive($"[color=DarkGray][font size=10]{args.Message.ToMarkup()}[/font][/color]"); // MAID rt-update
                message.PushNewline();
                message.Pop();

                _chatManager.ChatMessageToOne(ChatChannel.Emotes, message.ToString(), message.ToMarkup(), EntityUid.Invalid, false, actorComponent.PlayerSession.Channel, recordReplay: false);
            }
            else
            {
                var logLines = new List<string>();

                var textInfo = new CultureInfo("en-US", false).TextInfo;

                var item = Loc.GetString(
                    "examine-present",
                    ("name", args.IsSecondaryInfo ? displayName : textInfo.ToTitleCase(displayName)),
                    ("id", GetNetEntity(uid).Id),
                    ("size", 14));

                logLines.Add($"[color=DarkGray][font size=11]{item}[/font][/color]");

                logLines.Add($"[color=DarkGray][font size=10]{args.Message.ToMarkup()}[/font][/color]");
                var combinedLog = string.Join("\n", logLines);
                _chatManager.ChatMessageToOne(ChatChannel.Emotes, combinedLog, combinedLog, EntityUid.Invalid, false, actorComponent.PlayerSession.Channel, recordReplay: false);
            }
        }
    }
}
