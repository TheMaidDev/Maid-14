using Content.Shared._Maid.Extraction;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Admin debug verbs for testing extraction without going through the whole round flow:
/// skip the objectives, the zones, the code and the owner checks, and fast-forward the timers.
/// </summary>
public sealed partial class ExtractionSystem
{
    private void InitializeDebug()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetDebugVerbs);
    }

    private void OnGetDebugVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor)
            || !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Admin))
            return;

        var user = args.User;
        var target = args.Target;
        var session = actor.PlayerSession;

        if (HasComp<MobStateComponent>(target))
        {
            AddDebugVerb(args, "extraction-debug-verb-unlock", () => Reply(session, UnlockCommand(target)));
            AddDebugVerb(args, "extraction-debug-verb-pod-here", () => Reply(session, DebugLandPod(target)));
            AddDebugVerb(args, "extraction-debug-verb-skip", () => Reply(session, DebugSkip(target)));
            AddDebugVerb(args, "extraction-debug-verb-status", () => Reply(session, DebugStatus(target)));
            AddDebugVerb(args, "extraction-debug-verb-reset", () => Reply(session, ResetCommand(target)));
        }

        if (TryComp<DropPodComponent>(target, out var pod))
        {
            AddDebugVerb(args, "extraction-debug-verb-pod-board", () => Reply(session, DebugBoardPod((target, pod), user)));
            AddDebugVerb(args, "extraction-debug-verb-pod-launch", () => Reply(session, DebugLaunchPod((target, pod))));
        }

        if (TryComp<ExtractionDeviceComponent>(target, out var device))
            AddDebugVerb(args, "extraction-debug-verb-device-take", () => Reply(session, DebugTakeDevice((target, device), user)));
    }

    private void AddDebugVerb(GetVerbsEvent<Verb> args, string text, Action act)
    {
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(text),
            Category = VerbCategory.Debug,
            Act = act,
            Impact = LogImpact.Medium,
        });
    }

    private void Reply(ICommonSession session, string message)
    {
        _chatManager.DispatchServerMessage(session, message);
    }

    /// <summary>
    /// Makes sure <paramref name="uid"/> can extract, unlocking it like the admin command does.
    /// </summary>
    private Entity<AntagExtractableComponent> EnsureDebugExtractable(EntityUid uid)
    {
        UnlockCommand(uid);
        return (uid, Comp<AntagExtractableComponent>(uid));
    }

    /// <summary>
    /// Drops an already unlocked pod onto the target's tile right away, without a zone, a flight or a code.
    /// Nobody gets crushed. The old pod of the target, if any, is removed.
    /// </summary>
    private string DebugLandPod(EntityUid target)
    {
        var xform = Transform(target);
        if (xform.GridUid is not { } grid)
            return Loc.GetString("extraction-debug-no-grid", ("target", ToPrettyString(target).ToString()));

        var ent = EnsureDebugExtractable(target);
        if (ent.Comp.Pod is { } old)
            QueueDel(old);

        ent.Comp.Pod = null;
        ent.Comp.PodTarget = null;
        ent.Comp.PodAnnounceAt = null;
        ent.Comp.PodCalled = true;

        var coords = _transform.WithEntityId(xform.Coordinates, grid);
        if (LandDropPod(coords, ent.Comp.Landing, target, crush: false) is not { } pod)
            return Loc.GetString("extraction-debug-no-grid", ("target", ToPrettyString(target).ToString()));

        var podComp = Comp<DropPodComponent>(pod);
        podComp.Extractee = target;
        podComp.Locked = false;
        Dirty(pod, podComp);
        SetGlow((pod, podComp), DropPodGlow.Unlocked);
        ent.Comp.Pod = pod;

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Debug: unlocked extraction drop pod {ToPrettyString(pod)} landed for {ToPrettyString(target):player}");
        return Loc.GetString("extraction-debug-pod-landed", ("target", ToPrettyString(target).ToString()));
    }

    /// <summary>
    /// Fast-forwards whatever the target is waiting for: the pod flight, the launch countdown or the pod leaving on its own.
    /// </summary>
    private string DebugSkip(EntityUid target)
    {
        if (!TryComp<AntagExtractableComponent>(target, out var comp))
            return Loc.GetString("extraction-command-no-component", ("target", ToPrettyString(target).ToString()));

        var now = _timing.CurTime;
        if (comp.PodTarget != null)
        {
            comp.PodLandAt = now;
            comp.PodAnnounceAt = null;
            return Loc.GetString("extraction-debug-skip-flight");
        }

        if (comp.Pod is not { } pod || !TryComp<DropPodComponent>(pod, out var podComp))
            return Loc.GetString("extraction-debug-skip-nothing");

        if (podComp.Launching)
        {
            podComp.LaunchAt = now;
            return Loc.GetString("extraction-debug-skip-launch");
        }

        podComp.AutoLaunchAt = now;
        return Loc.GetString("extraction-debug-skip-auto");
    }

    private string DebugStatus(EntityUid target)
    {
        if (!TryComp<AntagExtractableComponent>(target, out var comp))
            return Loc.GetString("extraction-command-no-component", ("target", ToPrettyString(target).ToString()));

        var ent = new Entity<AntagExtractableComponent>(target, comp);
        var now = _timing.CurTime;
        var zones = comp.Zones.Count == 0 ? "-" : GetZonesLocation(ent);
        return Loc.GetString("extraction-debug-status",
            ("target", ToPrettyString(target).ToString()),
            ("unlocked", comp.Unlocked ? "+" : "-"),
            ("code", string.IsNullOrEmpty(comp.Code) ? "-" : comp.Code),
            ("requests", $"{comp.RequestsUsed}/{comp.MaxRequests}"),
            ("zones", zones),
            ("pod", GetPodStatus(ent, now)),
            ("device", comp.Device is { } device ? ToPrettyString(device).ToString() : "-"));
    }

    /// <summary>
    /// Unlocks the pod and makes the admin its extractee, so they can get in and fly away themselves.
    /// </summary>
    private string DebugBoardPod(Entity<DropPodComponent> pod, EntityUid user)
    {
        if (pod.Comp.Extractee is { } old && old != user && TryComp<AntagExtractableComponent>(old, out var oldComp) && oldComp.Pod == pod.Owner)
            oldComp.Pod = null;

        var ent = EnsureDebugExtractable(user);
        ent.Comp.Pod = pod.Owner;
        ent.Comp.PodCalled = true;

        pod.Comp.Extractee = user;
        pod.Comp.Locked = false;
        Dirty(pod);
        SetGlow(pod, pod.Comp.Launching ? DropPodGlow.Sealed : DropPodGlow.Unlocked);
        UpdatePodUi(pod);

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Debug: {ToPrettyString(user):player} took over extraction drop pod {ToPrettyString(pod.Owner)}");
        return Loc.GetString("extraction-debug-pod-boarded");
    }

    /// <summary>
    /// Lifts the pod off right now, with whoever is inside.
    /// </summary>
    private string DebugLaunchPod(Entity<DropPodComponent> pod)
    {
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Debug: extraction drop pod {ToPrettyString(pod.Owner)} launched by an admin");
        LaunchPod(pod);
        return Loc.GetString("extraction-debug-pod-launched");
    }

    /// <summary>
    /// Makes the admin the owner of the device, unlocking extraction for them.
    /// </summary>
    private string DebugTakeDevice(Entity<ExtractionDeviceComponent> device, EntityUid user)
    {
        var ent = EnsureDebugExtractable(user);
        ent.Comp.Device = device.Owner;
        ent.Comp.DeviceGiven = true;

        device.Comp.Owner = user;
        Dirty(device);

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Debug: {ToPrettyString(user):player} took over extraction device {ToPrettyString(device.Owner)}");
        return Loc.GetString("extraction-debug-device-taken");
    }
}
