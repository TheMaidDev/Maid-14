// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gluesniffler <linebarrelerenthusiast@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Text;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Traumas;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Components;
using Content.Shared._Shitmed.Medical.Surgery.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Components;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Systems;
using Content.Shared._Shitmed.PartStatus.Events;
using Content.Shared.Body.Part;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Utility;

using Content.Goobstation.Common.Examine; // Goobstation Change
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Content.Shared.HealthExaminable;
using Robust.Shared.Prototypes;

namespace Content.Server._Shitmed.PartStatus;

public sealed class PartStatusSystem : EntitySystem
{
    [Dependency] private readonly WoundSystem _woundSystem = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ExamineSystemShared _examineSystem = default!;

    private static readonly IReadOnlyList<BodyPartType> BodyPartOrder = new List<BodyPartType>
    {
        BodyPartType.Head,
        BodyPartType.Chest,
        BodyPartType.Arm,
        BodyPartType.Hand,
        BodyPartType.Groin,
        BodyPartType.Leg,
        BodyPartType.Foot,
    }.AsReadOnly();

    private static readonly List<BodyPartSymmetry> SymmetryPriority =
    [
        BodyPartSymmetry.Left,
        BodyPartSymmetry.Right,
        BodyPartSymmetry.None,
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GetPartStatusEvent>(OnGetPartStatus);
        SubscribeLocalEvent<HealthExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetPartStatus(GetPartStatusEvent message, EntitySessionEventArgs args)
    {
        var entity = GetEntity(message.Uid);
        //Maid edit start
        if (_mobStateSystem.IsIncapacitated(entity)
            || !TryComp<HealthExaminableComponent>(entity, out var healthExaminable)
            || !TryComp<DamageableComponent>(entity, out var damage))
            return;

        var markup = CreateMarkup(entity, entity, healthExaminable, damage);

        var completedEvent = new ExamineCompletedEvent(markup, entity, entity, isSecondaryInfo: true);
        RaiseLocalEvent(entity, completedEvent);
        //Maid edit end
    }


    private void OnGetExamineVerbs(EntityUid uid, HealthExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return;

        var detailsRange = _examineSystem.IsInDetailsRange(args.User, uid);

        var verb = new ExamineVerb()
        {
            Act = () =>
            {
                var markup = CreateMarkup(uid, args.User, component, damage);
                //Maid edit start
                var markupString = markup.ToMarkup();
                markupString = markupString.Replace("[font size=11]", "").Replace("[font size=10]", "").Replace("[/font]", "").Replace("[bold]", "").Replace("[/bold]", "");

                var tooltipMessage = FormattedMessage.FromMarkupOrThrow($"[font size=10]{markupString}[/font]");
                _examineSystem.SendExamineTooltip(args.User, uid, tooltipMessage, false, false);
                //Maid edit end
                var examineCompletedEvent = new ExamineCompletedEvent(markup, uid, args.User, true); // Goobstation
                RaiseLocalEvent(uid, examineCompletedEvent); // Goobstation
            },
            Text = Loc.GetString("health-examinable-verb-text"),
            Category = VerbCategory.Examine,
            Disabled = !detailsRange,
            Message = detailsRange ? null : Loc.GetString("health-examinable-verb-disabled"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png"))
        };

        args.Verbs.Add(verb);
    }

    public FormattedMessage CreateMarkup(EntityUid uid, EntityUid examiner, HealthExaminableComponent component, DamageableComponent damage)
    {
        if (!_bodySystem.TryGetRootPart(uid, out var rootPart))
            return new FormattedMessage();

        var partStatusSet = CollectPartStatuses(rootPart.Value);
        var text = GetExamineText(uid, examiner, partStatusSet);
        // Anything else want to add on to this?
        RaiseLocalEvent(uid, new HealthBeingExaminedEvent(text), true);

        return text;
    }


    private HashSet<PartStatus> CollectPartStatuses(Entity<BodyPartComponent> rootPart)
    {
        var partStatusSet = new HashSet<PartStatus>();

        foreach (var woundable in _woundSystem.GetAllWoundableChildren(rootPart))
        {
            if (!TryComp<BodyPartComponent>(woundable, out var bodyPartComponent))
                continue;
            //Maid edit start
            var partLocKey = bodyPartComponent.Symmetry != BodyPartSymmetry.None
                ? $"inspect-part-status-{bodyPartComponent.Symmetry}{bodyPartComponent.PartType}"
                : $"inspect-part-status-{bodyPartComponent.PartType}";

            var partName = Loc.GetString(partLocKey);

            var bone = woundable.Comp.Bone.ContainedEntities.FirstOrNull();
            var boneSeverity = bone != null && TryComp<BoneComponent>(bone, out var boneComp)
                ? boneComp.BoneSeverity
                : BoneSeverity.Normal;

            //Maid edit end
            var (damageSeverities, isBleeding) = AnalyzeWounds(woundable);

            partStatusSet.Add(new PartStatus(
                bodyPartComponent.PartType,
                bodyPartComponent.Symmetry,
                partName,
                woundable.Comp.WoundableSeverity,
                damageSeverities,
                boneSeverity,
                isBleeding));
        }

        return partStatusSet;
    }

    private (Dictionary<string, WoundSeverity> DamageSeverities, bool IsBleeding) AnalyzeWounds(
        Entity<WoundableComponent> woundable)
    {
        var damageSeverities = new Dictionary<string, WoundSeverity>();
        var isBleeding = false;

        foreach (var wound in _woundSystem.GetWoundableWounds(woundable))
        {
            if (wound.Comp.DamageGroup == null
                || wound.Comp.WoundSeverity == WoundSeverity.Healed)
                continue;

            if (!damageSeverities.TryGetValue(wound.Comp.DamageType, out var existingSeverity) ||
                wound.Comp.WoundSeverity > existingSeverity)
                damageSeverities[_proto.Index(wound.Comp.DamageGroup).ID] = wound.Comp.WoundSeverity;

            if (TryComp<BleedInflicterComponent>(wound, out var bleeds) && bleeds.IsBleeding)
                isBleeding = true;
        }

        return (damageSeverities, isBleeding);
    }

    private FormattedMessage GetExamineText(EntityUid entity,
        EntityUid examiner,
        HashSet<PartStatus> partStatusSet)
    {
        var message = new FormattedMessage();
        var titlestring = entity == examiner
            ? "inspect-part-status-title"
            : "inspect-part-status-title-other";


        message.AddMarkupPermissive(Loc.GetString(titlestring, ("entity", entity)));
        message.PushNewline();

        CreateBodyPartMessage(partStatusSet, entity == examiner, entity, ref message);

        return message;
    }

    private void CreateBodyPartMessage(HashSet<PartStatus> partStatusSet,
        bool inspectingSelf,
        EntityUid examinedEntity,
        ref FormattedMessage message)
    {
        var orderedParts = BodyPartOrder
            .SelectMany(partType => partStatusSet.Where(p => p.PartType == partType)
                .ToList()
                .OrderBy(p => SymmetryPriority.IndexOf(p.PartSymmetry)))
            .ToList();

        foreach (var partStatus in orderedParts)
        {
            var statusDescription = BuildStatusDescription(partStatus, inspectingSelf);

            var locString = "inspect-part-status-line";

            message.AddMarkupPermissive(Loc.GetString(locString,
                ("entity", examinedEntity),
                ("isSelf", inspectingSelf),
                ("partType", partStatus.PartType.ToString()),
                ("part", partStatus.PartName),
                ("status", statusDescription)));

            message.PushNewline();
        }
    }

    private string BuildStatusDescription(PartStatus partStatus, bool inspectingSelf)
    {
        var sb = new StringBuilder();
        //Maid edit start
        var groups = new List<string>();

        // Bone trauma descriptions
        var boneDescriptions = GetBoneDescriptions(partStatus, inspectingSelf);
        if (boneDescriptions.Count > 0)
        {
            groups.AddRange(boneDescriptions);
        }

        // Damage group descriptions (Brute/Burn)
        var damageDescriptions = GetDamageGroupDescriptions(partStatus.DamageSeverities, partStatus.PartType, inspectingSelf);
        if (damageDescriptions.Count > 0)
        {
            groups.AddRange(damageDescriptions);
        }

        // Bleeding status
        if (partStatus.Bleeding)
        {
            var severityKey = partStatus.PartSeverity switch
            {
                WoundableSeverity.Minor => "inspect-wound-Bleeding-minor",
                WoundableSeverity.Moderate => "inspect-wound-Bleeding-moderate",
                WoundableSeverity.Severe => "inspect-wound-Bleeding-severe",
                WoundableSeverity.Critical => "inspect-wound-Bleeding-severe",
                WoundableSeverity.Mangled => "inspect-wound-Bleeding-severe",
                WoundableSeverity.Severed => "inspect-wound-Bleeding-severe",
                _ => "inspect-wound-Bleeding-moderate"
            };
            var localeKey = inspectingSelf
                ? severityKey.Replace("inspect-wound-", "self-inspect-wound-")
                : severityKey;

            groups.Add(Loc.GetString(localeKey));
        }

        if (groups.Count == 0)
        {
            sb.Append(Loc.GetString("inspect-part-status-fine"));
        }
        else
        {
            if (groups.Count > 1)
            {
                groups[^1] = Loc.GetString("inspect-part-status-and") + groups[^1];
            }
            sb.Append(string.Join(Loc.GetString("inspect-part-status-comma") + " ", groups));
        }
        //Maid edit end
        return sb.ToString();
    }

    private List<string> GetDamageGroupDescriptions(Dictionary<string, WoundSeverity> damageSeverities, BodyPartType partType, bool inspectingSelf)
    {
        var descriptions = new List<string>();
        foreach (var (type, severity) in damageSeverities)
        {
            if (type is not ("Brute" or "Burn"))
                continue;

            var cappedSeverity = severity > WoundSeverity.Severe ? WoundSeverity.Severe : severity;
            var localeKey = $"inspect-wound-{type}-{cappedSeverity.ToString().ToLower()}";
            //Maid edit start
            if (inspectingSelf)
            {
                localeKey = localeKey.Replace("inspect-wound-", "self-inspect-wound-");
            }
            //Maid edit end
            descriptions.Add(Loc.GetString(localeKey, ("partType", partType.ToString())));
        }

        return descriptions;
    }

    private List<string> GetBoneDescriptions(PartStatus partStatus, bool inspectingSelf)
    {
        var descriptions = new List<string>();

        // TODO: Dehardcode this guscode from bone traumas when we actually have more organ traumas.

        // Add bone trauma
        if (partStatus.BoneSeverity > BoneSeverity.Normal)
        {
            //Maid edit start
            var severityStr = partStatus.BoneSeverity switch
            {
                BoneSeverity.Damaged => "Damaged",
                BoneSeverity.Cracked => "Cracked",
                BoneSeverity.Broken => "Broken",
                _ => "Damaged"
            };
            var localeKey = inspectingSelf
                ? $"self-inspect-trauma-BoneDamage-{severityStr}"
                : $"inspect-trauma-BoneDamage-{severityStr}";
            descriptions.Add(Loc.GetString(localeKey,
                ("part", partStatus.PartName),
                ("partType", partStatus.PartType.ToString())));
            //Maid edit end
        }

        return descriptions;
    }
}
