using System.Linq;
using Content.Shared._Maid.Extraction;
using Content.Shared.Database;
using Content.Shared.Mind;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Extraction device: handing it out and its UI.
/// </summary>
public sealed partial class ExtractionSystem
{
    private static readonly string[] DevicePockets = { "pocket1", "pocket2" };

    private void InitializeDevice()
    {
        SubscribeLocalEvent<ExtractionDeviceComponent, BoundUIOpenedEvent>(OnDeviceUiOpened);
        SubscribeLocalEvent<ExtractionDeviceComponent, ExtractionDeviceRefreshMessage>(OnDeviceRefresh);
        SubscribeLocalEvent<ExtractionDeviceComponent, ExtractionDeviceRequestZoneMessage>(OnDeviceRequestZone);
        SubscribeLocalEvent<ExtractionDeviceComponent, ExtractionDeviceCallPodMessage>(OnDeviceCallPod);
    }

    /// <summary>
    /// Puts the device into the backpack, a free pocket or a hand, in that order. Given only once.
    /// </summary>
    private void GiveDevice(Entity<AntagExtractableComponent> ent)
    {
        ent.Comp.DeviceGiven = true;
        if (ent.Comp.DevicePrototype is not { } proto)
            return;

        var user = ent.Owner;
        var device = Spawn(proto, Transform(user).Coordinates);
        var deviceComp = EnsureComp<ExtractionDeviceComponent>(device);
        deviceComp.Owner = user;
        Dirty(device, deviceComp);
        EnsureComp<ExtractionGearComponent>(device);
        ent.Comp.Device = device;

        if (_inventory.TryGetSlotEntity(user, "back", out var back)
            && _storage.Insert(back.Value, device, out _, playSound: false))
            return;

        foreach (var pocket in DevicePockets)
        {
            if (_inventory.TryEquip(user, device, pocket, silent: true))
                return;
        }

        _hands.TryPickupAnyHand(user, device);
    }

    private bool TryGetDeviceOwner(Entity<ExtractionDeviceComponent> device, EntityUid actor, out Entity<AntagExtractableComponent> owner)
    {
        owner = default;
        if (device.Comp.Owner is not { } uid
            || uid != actor
            || !TryComp<AntagExtractableComponent>(uid, out var comp))
            return false;

        owner = (uid, comp);
        return true;
    }

    private void OnDeviceUiOpened(Entity<ExtractionDeviceComponent> ent, ref BoundUIOpenedEvent args)
    {
        RequestDeviceUi(ent);
    }

    private void OnDeviceRefresh(Entity<ExtractionDeviceComponent> ent, ref ExtractionDeviceRefreshMessage args)
    {
        RequestDeviceUi(ent);
    }

    private void OnDeviceRequestZone(Entity<ExtractionDeviceComponent> ent, ref ExtractionDeviceRequestZoneMessage args)
    {
        if (TryGetDeviceOwner(ent, args.Actor, out var owner))
            TryRequestZone(owner);

        RequestDeviceUi(ent);
    }

    private void OnDeviceCallPod(Entity<ExtractionDeviceComponent> ent, ref ExtractionDeviceCallPodMessage args)
    {
        if (TryGetDeviceOwner(ent, args.Actor, out var owner))
            TryCallPod(owner);

        RequestDeviceUi(ent);
    }

    /// <summary>
    /// Updates the device UI now, or once its cooldown is over if it was updated just before.
    /// </summary>
    private void RequestDeviceUi(Entity<ExtractionDeviceComponent> device)
    {
        var now = _timing.CurTime;
        if (now < device.Comp.NextUiUpdate)
        {
            device.Comp.UiUpdatePending = true;
            return;
        }

        device.Comp.UiUpdatePending = false;
        device.Comp.NextUiUpdate = now + device.Comp.UiUpdateCooldown;
        UpdateDeviceUi(device);
    }

    private void UpdatePendingDeviceUis(TimeSpan now)
    {
        var query = EntityQueryEnumerator<ExtractionDeviceComponent>();
        while (query.MoveNext(out var uid, out var device))
        {
            if (device.UiUpdatePending && now >= device.NextUiUpdate)
                RequestDeviceUi((uid, device));
        }
    }

    private void UpdateDeviceUi(Entity<ExtractionDeviceComponent> device)
    {
        // only the owner may see it, and the state goes to them alone
        if (device.Comp.Owner is not { } uid
            || !TryComp<AntagExtractableComponent>(uid, out var comp)
            || !_ui.IsUiOpen(device.Owner, ExtractionDeviceUiKey.Key, uid))
            return;

        var ent = new Entity<AntagExtractableComponent>(uid, comp);
        var now = _timing.CurTime;

        var objectives = new List<ExtractionObjectiveEntry>();
        var completed = 0;
        if (_mind.TryGetMind(uid, out var mindId, out var mind))
        {
            foreach (var (objective, done, required) in GetCountedObjectives(ent, (mindId, mind)))
            {
                var title = _objectives.GetInfo(objective, mindId, mind)?.Title ?? Name(objective);
                objectives.Add(new ExtractionObjectiveEntry(title, done, required));
                if (done)
                    completed++;
            }
        }

        var total = objectives.Count;
        var percent = total == 0 ? 0 : (int) MathF.Round(completed * 100f / total);
        var progress = comp.ThresholdMode == ExtractionThresholdMode.Count
            ? Loc.GetString("extraction-device-progress-count",
                ("completed", completed), ("total", total), ("needed", (int) comp.Threshold))
            : Loc.GetString("extraction-device-progress-percent",
                ("completed", completed), ("total", total), ("percent", percent), ("needed", (int) comp.Threshold));

        // the first request is not a re-roll
        var rerollsLeft = Math.Max(0, comp.MaxRequests - Math.Max(comp.RequestsUsed, 1));

        var zone = comp.Zones.Any(z => !TerminatingOrDeleted(z))
            ? "\n" + GetZonesLocation(ent)
            : Loc.GetString("extraction-device-zone-none");

        var state = new ExtractionDeviceUiState(
            comp.Unlocked,
            objectives,
            progress,
            rerollsLeft,
            comp.Unlocked ? comp.Code : null,
            zone,
            GetPodStatus(ent, now),
            comp.Unlocked && !comp.Extracted && comp.RequestsUsed < comp.MaxRequests,
            CanCallPod(ent, now));

        _ui.ServerSendUiMessage(device.Owner, ExtractionDeviceUiKey.Key, state, uid);
    }

    private string GetPodStatus(Entity<AntagExtractableComponent> ent, TimeSpan now)
    {
        if (ent.Comp.PodTarget != null)
            return Loc.GetString("extraction-device-pod-flying", ("seconds", SecondsLeft(ent.Comp.PodLandAt, now)));

        if (ent.Comp.Pod is { } pod && TryComp<DropPodComponent>(pod, out var podComp) && !TerminatingOrDeleted(pod))
        {
            if (podComp.Launching)
                return Loc.GetString("extraction-device-pod-launching", ("seconds", SecondsLeft(podComp.LaunchAt, now)));

            if (podComp.AutoLaunchAt is { } leaveAt)
            {
                var left = leaveAt - now;
                return Loc.GetString("extraction-device-pod-landed",
                    ("time", $"{(int) Math.Max(0, left.TotalMinutes)}:{Math.Max(0, left.Seconds):00}"));
            }

            return Loc.GetString("extraction-device-pod-landed-forever");
        }

        if (ent.Comp.PodCalled)
            return Loc.GetString("extraction-device-pod-gone");

        return Loc.GetString("extraction-device-pod-none");
    }

    private static int SecondsLeft(TimeSpan at, TimeSpan now)
    {
        return Math.Max(0, (int) Math.Ceiling((at - now).TotalSeconds));
    }
}
