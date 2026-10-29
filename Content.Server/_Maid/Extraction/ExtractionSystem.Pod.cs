using System.Numerics;
using Content.Shared._Maid.Extraction;
using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Drop pod part of extraction: calling the pod into the zone, its landing, the code keypad,
/// the launch countdown and the lift-off.
/// </summary>
public sealed partial class ExtractionSystem
{
    private void InitializePod()
    {
        SubscribeLocalEvent<AntagExtractableComponent, LaunchDropPodActionEvent>(OnLaunchPod);

        SubscribeLocalEvent<DropPodComponent, MapInitEvent>(OnPodMapInit);
        SubscribeLocalEvent<DropPodComponent, EntInsertedIntoContainerMessage>(OnPodInserted);
        SubscribeLocalEvent<DropPodComponent, EntRemovedFromContainerMessage>(OnPodRemoved);
        SubscribeLocalEvent<DropPodComponent, ComponentShutdown>(OnPodShutdown);
        SubscribeLocalEvent<DropPodComponent, BoundUIOpenedEvent>(OnPodUiOpened);
        SubscribeLocalEvent<DropPodComponent, DropPodKeypadMessage>(OnKeypad);
        SubscribeLocalEvent<DropPodComponent, DropPodKeypadClearMessage>(OnKeypadClear);
        SubscribeLocalEvent<DropPodComponent, DropPodKeypadEnterMessage>(OnKeypadEnter);
    }

    #region Calling and landing

    /// <summary>
    /// The pod can be called once, and only from inside one of the extraction zones.
    /// After the call the extractee may leave, the pod still lands where it was called.
    /// </summary>
    private bool CanCallPod(Entity<AntagExtractableComponent> ent, TimeSpan now)
    {
        if (!ent.Comp.Unlocked || ent.Comp.Extracted || ent.Comp.PodCalled)
            return false;

        foreach (var zone in ent.Comp.Zones)
        {
            if (TryComp<ExtractionZoneComponent>(zone, out var zoneComp) && _zone.IsInZone((zone, zoneComp), ent.Owner))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calls the pod to the tile the extractee stands on, once, from inside their zone.
    /// </summary>
    public void TryCallPod(Entity<AntagExtractableComponent> ent)
    {
        var now = _timing.CurTime;
        var user = ent.Owner;

        if (!CanCallPod(ent, now))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.Messages.NotInZone), user, user, PopupType.SmallCaution);
            return;
        }

        var xform = Transform(user);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        // the pod lands on the tile the call was made from
        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        ent.Comp.PodCalled = true;
        ent.Comp.PodTarget = _map.GridTileToLocal(grid, gridComp, tile);
        ent.Comp.PodLandAt = now + ent.Comp.PodFlightTime;
        ent.Comp.PodAnnounceAt = now + _random.Next(ent.Comp.PodAnnounceDelayMin, ent.Comp.PodAnnounceDelayMax);

        Notify(ent, Loc.GetString(ent.Comp.Messages.PodCalled, ("seconds", (int) ent.Comp.PodFlightTime.TotalSeconds)));
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} called an extraction drop pod to {ent.Comp.PodTarget} ({GetLocation(user)})");
    }

    /// <summary>
    /// Handles the destroyed pod and the announcement, returns where to land once the pod arrives.
    /// </summary>
    private EntityCoordinates? UpdatePodCall(Entity<AntagExtractableComponent> ent, TimeSpan now)
    {
        if (ent.Comp.Pod is { } pod && TerminatingOrDeleted(pod))
        {
            ent.Comp.Pod = null;
            Notify(ent, Loc.GetString(ent.Comp.Messages.PodDestroyed));
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"Extraction drop pod of {ToPrettyString(ent.Owner):player} was destroyed");
        }

        if (ent.Comp.PodTarget is not { } target)
            return null;

        // the target grid is gone while the pod is on its way
        if (TerminatingOrDeleted(target.EntityId))
        {
            FailPodLanding(ent, now);
            return null;
        }

        if (ent.Comp.PodAnnounceAt is { } announceAt && now >= announceAt)
        {
            ent.Comp.PodAnnounceAt = null;
            if (_station.GetOwningStation(target.EntityId) is { } station)
            {
                _chat.DispatchStationAnnouncement(station,
                    Loc.GetString(ent.Comp.Messages.PodAnnouncement, ("location", GetLocation(target))),
                    Loc.GetString(ent.Comp.Messages.AnnouncementSender),
                    colorOverride: ent.Comp.Messages.AnnouncementColor);
            }
        }

        if (now < ent.Comp.PodLandAt)
            return null;

        ent.Comp.PodTarget = null;
        return target;
    }

    /// <summary>
    /// The pod could not land. The call is spent all the same, only the admin reset command gives another one.
    /// </summary>
    private void FailPodLanding(Entity<AntagExtractableComponent> ent, TimeSpan now)
    {
        ent.Comp.PodTarget = null;
        ent.Comp.PodAnnounceAt = null;

        Notify(ent, Loc.GetString(ent.Comp.Messages.PodLost));
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Extraction drop pod of {ToPrettyString(ent.Owner):player} lost its landing point");
    }

    private void LandPod(Entity<AntagExtractableComponent> ent, EntityCoordinates target)
    {
        if (LandDropPod(target, ent.Comp.Landing, ent.Owner) is not { } pod)
        {
            FailPodLanding(ent, _timing.CurTime);
            return;
        }

        var podComp = Comp<DropPodComponent>(pod);
        podComp.Extractee = ent.Owner;
        Dirty(pod, podComp);
        ent.Comp.Pod = pod;

        Notify(ent, Loc.GetString(ent.Comp.Messages.PodLanded,
            ("code", ent.Comp.Code),
            ("minutes", (int) Math.Ceiling(podComp.AutoLaunchDelay.TotalMinutes))));
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Extraction drop pod {ToPrettyString(pod)} of {ToPrettyString(ent.Owner):player} landed at {target}");
    }

    /// <summary>
    /// Drops a pod onto the tile at <paramref name="target"/>: clears the tile if asked, leaves the rubble decal,
    /// plays the impact and shakes nearby screens. Returns null when there is no grid to land on.
    /// </summary>
    public EntityUid? LandDropPod(EntityCoordinates target, DropPodLanding landing, EntityUid? cause = null)
    {
        if (!target.IsValid(EntityManager)
            || TerminatingOrDeleted(target.EntityId)
            || !TryComp<MapGridComponent>(target.EntityId, out var gridComp))
            return null;

        var grid = target.EntityId;
        var tile = _map.TileIndicesFor(grid, gridComp, target);
        var center = _map.GridTileToLocal(grid, gridComp, tile);

        if (landing.CrushLiving)
            CrushLiving(cause, grid, gridComp, tile);

        var pod = Spawn(landing.PodPrototype, center);
        var podComp = EnsureComp<DropPodComponent>(pod);
        podComp.LandedAt = _timing.CurTime;
        Dirty(pod, podComp);

        if (podComp.AutoLaunchDelay > TimeSpan.Zero)
            podComp.AutoLaunchAt = _timing.CurTime + podComp.AutoLaunchDelay;

        // rubble where the pod hit, it stays even if the pod is gone
        var decalPos = new Vector2(tile.X, tile.Y) + podComp.LandingDecalOffset;
        _decals.TryAddDecal(podComp.LandingDecal, new EntityCoordinates(grid, decalPos), out _, cleanable: true);

        PlayImpactSound(landing, center);
        ShakeScreens(landing, center);
        return pod;
    }

    /// <summary>
    /// Whatever is alive and standing on the landing tile gets gibbed. Objects, the dead and
    /// anyone inside lockers or other containers are left alone.
    /// </summary>
    private void CrushLiving(EntityUid? cause, EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var immune = new HashSet<EntityUid>();
        foreach (var victim in _lookup.GetLocalEntitiesIntersecting(grid, tile, flags: LookupFlags.Uncontained, gridComp: gridComp))
        {
            if (immune.Contains(victim)
                || TerminatingOrDeleted(victim)
                || EntityManager.IsQueuedForDeletion(victim)
                || !TryComp<MobStateComponent>(victim, out var mobState)
                || _mobState.IsDead(victim, mobState))
                continue;

            _adminLog.Add(LogType.Gib, LogImpact.High,
                $"{ToPrettyString(victim):player} was crushed by the extraction drop pod of {ToPrettyString(cause):player}");

            if (TryComp<BodyComponent>(victim, out var body))
                immune.UnionWith(_body.GibBody(victim, body: body));
            else
                _destructible.DestroyEntity(victim);
        }
    }

    /// <summary>
    /// Near players hear the positional impact, everyone else on the map the distant one, like explosions and meteors.
    /// </summary>
    private void PlayImpactSound(DropPodLanding landing, EntityCoordinates target)
    {
        var mapCoords = _transform.ToMapCoordinates(target);
        var nearRange = landing.ImpactNearRange;

        var nearFilter = Filter.Pvs(target).AddInRange(mapCoords, nearRange);
        _audio.PlayStatic(landing.ImpactSound, nearFilter, target, true, landing.ImpactSound.Params);

        var farFilter = Filter.Empty().AddInMap(mapCoords.MapId).RemoveInRange(mapCoords, nearRange);
        _audio.PlayGlobal(landing.ImpactSoundFar, farFilter, true, landing.ImpactSoundFar.Params);
    }

    /// <summary>
    /// Screens shake near the landing, stronger closer to it, like with explosions.
    /// </summary>
    private void ShakeScreens(DropPodLanding landing, EntityCoordinates target)
    {
        var epicenter = _transform.ToMapCoordinates(target);
        var range = landing.ShakeRange;
        if (range <= 0f)
            return;

        foreach (var player in Filter.Empty().AddInRange(epicenter, range).Recipients)
        {
            if (player.AttachedEntity is not { } uid)
                continue;

            var delta = epicenter.Position - _transform.GetWorldPosition(uid);
            if (delta.EqualsApprox(Vector2.Zero))
                delta = new Vector2(0.01f, 0f);

            var effect = landing.ShakeStrength * (1f - delta.Length() / range);
            if (effect > 0.01f)
                _recoil.KickCamera(uid, -delta.Normalized() * effect);
        }
    }

    #endregion

    #region Glow

    private void OnPodMapInit(Entity<DropPodComponent> ent, ref MapInitEvent args)
    {
        SetGlow(ent, ent.Comp.Locked ? DropPodGlow.Locked : DropPodGlow.Unlocked);
    }

    private void SetGlow(Entity<DropPodComponent> ent, DropPodGlow glow)
    {
        if (ent.Comp.GlowColors.TryGetValue(glow, out var color))
            _pointLight.SetColor(ent.Owner, color);
    }

    #endregion

    #region Keypad

    private void OnPodUiOpened(Entity<DropPodComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdatePodUi(ent);
    }

    private void OnKeypad(Entity<DropPodComponent> ent, ref DropPodKeypadMessage args)
    {
        if (!ent.Comp.Locked || ent.Comp.EnteredCode.Length >= GetCodeLength(ent))
            return;

        ent.Comp.EnteredCode += Math.Clamp(args.Value, 0, 9).ToString();
        ent.Comp.WrongCode = false;
        _audio.PlayPvs(ent.Comp.KeypadSound, ent.Owner);
        UpdatePodUi(ent);
    }

    private void OnKeypadClear(Entity<DropPodComponent> ent, ref DropPodKeypadClearMessage args)
    {
        ent.Comp.EnteredCode = string.Empty;
        ent.Comp.WrongCode = false;
        _audio.PlayPvs(ent.Comp.KeypadSound, ent.Owner);
        UpdatePodUi(ent);
    }

    private void OnKeypadEnter(Entity<DropPodComponent> ent, ref DropPodKeypadEnterMessage args)
    {
        if (!ent.Comp.Locked)
            return;

        var code = CompOrNull<AntagExtractableComponent>(ent.Comp.Extractee)?.Code;
        var correct = !string.IsNullOrEmpty(code) && ent.Comp.EnteredCode == code;
        ent.Comp.EnteredCode = string.Empty;

        if (correct)
        {
            ent.Comp.Locked = false;
            ent.Comp.WrongCode = false;
            Dirty(ent);
            SetGlow(ent, DropPodGlow.Unlocked);
            _audio.PlayPvs(ent.Comp.UnlockSound, ent.Owner);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Actor):player} unlocked extraction drop pod {ToPrettyString(ent.Owner)}");
        }
        else
        {
            ent.Comp.WrongCode = true;
            _audio.PlayPvs(ent.Comp.WrongCodeSound, ent.Owner);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Actor):player} entered a wrong code on extraction drop pod {ToPrettyString(ent.Owner)}");
        }

        UpdatePodUi(ent);
    }

    private int GetCodeLength(Entity<DropPodComponent> ent)
    {
        return CompOrNull<AntagExtractableComponent>(ent.Comp.Extractee)?.CodeLength ?? 6;
    }

    private void UpdatePodUi(Entity<DropPodComponent> ent)
    {
        var status = ent.Comp.Launching ? DropPodStatus.Launching
            : !ent.Comp.Locked ? DropPodStatus.Unlocked
            : ent.Comp.WrongCode ? DropPodStatus.WrongCode
            : DropPodStatus.Locked;

        _ui.SetUiState(ent.Owner, DropPodUiKey.Key, new DropPodUiState(status, ent.Comp.EnteredCode.Length, GetCodeLength(ent)));
    }

    #endregion

    #region Launch

    private void OnPodInserted(Entity<DropPodComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != SharedEntityStorageSystem.ContainerName || args.Entity != ent.Comp.Extractee)
            return;

        // the same action entity is reused on every entry
        _actions.AddAction(args.Entity, ref ent.Comp.LaunchActionEntity, ent.Comp.LaunchAction);
        _popup.PopupEntity(Loc.GetString(ent.Comp.InsideMessage), args.Entity, args.Entity);
    }

    private void OnPodRemoved(Entity<DropPodComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != SharedEntityStorageSystem.ContainerName || args.Entity != ent.Comp.Extractee)
            return;

        _actions.RemoveAction(args.Entity, ent.Comp.LaunchActionEntity);

        if (!ent.Comp.Launching)
            return;

        // the extractee left the pod during the countdown: no launch without them
        StopLaunch(ent);
        _popup.PopupEntity(Loc.GetString(ent.Comp.LaunchCancelledMessage), args.Entity, args.Entity, PopupType.MediumCaution);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Launch of extraction drop pod {ToPrettyString(ent.Owner)} was cancelled, {ToPrettyString(args.Entity):player} left it");
    }

    private void OnPodShutdown(Entity<DropPodComponent> ent, ref ComponentShutdown args)
    {
        _audio.Stop(ent.Comp.EngineSoundStream);
        StopLaunchSparks(ent);
    }

    private void StartLaunchSparks(Entity<DropPodComponent> pod)
    {
        if (pod.Comp.LaunchSparksEffect is not { } effect || pod.Comp.LaunchSparks.Count > 0)
            return;

        foreach (var offset in pod.Comp.LaunchSparksOffsets)
        {
            pod.Comp.LaunchSparks.Add(SpawnAttachedTo(effect, new EntityCoordinates(pod.Owner, offset)));
        }
    }

    private void StopLaunchSparks(Entity<DropPodComponent> pod)
    {
        foreach (var spark in pod.Comp.LaunchSparks)
        {
            QueueDel(spark);
        }

        pod.Comp.LaunchSparks.Clear();
    }

    private void OnLaunchPod(Entity<AntagExtractableComponent> ent, ref LaunchDropPodActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = ent.Owner;

        if (!_container.TryGetContainingContainer(user, out var container)
            || !TryComp<DropPodComponent>(container.Owner, out var pod)
            || pod.Extractee != user)
        {
            var notInside = CompOrNull<DropPodComponent>(ent.Comp.Pod)?.NotInsideMessage ?? "extraction-pod-not-inside";
            _popup.PopupEntity(Loc.GetString(notInside), user, user, PopupType.SmallCaution);
            return;
        }

        if (pod.Launching)
            return;

        pod.Launching = true;
        pod.LaunchAt = _timing.CurTime + pod.LaunchDelay;
        pod.LastCountdownSecond = 0;
        Dirty(container.Owner, pod);
        UpdatePodUi((container.Owner, pod));
        SetGlow((container.Owner, pod), DropPodGlow.Sealed);

        if (pod.EngineSound != null)
            pod.EngineSoundStream = _audio.PlayPvs(pod.EngineSound, container.Owner)?.Entity;

        _popup.PopupEntity(Loc.GetString(pod.LaunchingMessage, ("seconds", (int) pod.LaunchDelay.TotalSeconds)), user, user, PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} started the launch of extraction drop pod {ToPrettyString(container.Owner)}");
    }

    private void StopLaunch(Entity<DropPodComponent> pod)
    {
        pod.Comp.Launching = false;
        pod.Comp.EngineSoundStream = _audio.Stop(pod.Comp.EngineSoundStream);
        StopLaunchSparks(pod);
        Dirty(pod);
        UpdatePodUi(pod);
        SetGlow(pod, pod.Comp.Locked ? DropPodGlow.Locked : DropPodGlow.Unlocked);
    }

    private void UpdatePods(TimeSpan now)
    {
        var query = EntityQueryEnumerator<DropPodComponent>();
        while (query.MoveNext(out var uid, out var pod))
        {
            if (!pod.Launching)
            {
                UpdateAutoLaunch((uid, pod), now);
                continue;
            }

            var remaining = (int) Math.Ceiling((pod.LaunchAt - now).TotalSeconds);
            if (remaining <= 0)
            {
                LaunchPod((uid, pod));
                continue;
            }

            if (pod.LastCountdownSecond == remaining)
                continue;

            pod.LastCountdownSecond = remaining;

            if (remaining <= pod.LaunchSparksSeconds)
                StartLaunchSparks((uid, pod));

            if (pod.CountdownSound != null)
                _audio.PlayPvs(pod.CountdownSound, uid);

            if (pod.CountdownPopups.Contains(remaining) && pod.Extractee is { } occupant)
                _popup.PopupEntity(remaining.ToString(), occupant, occupant, PopupType.Large);
        }
    }

    /// <summary>
    /// A pod left standing too long leaves the station on its own, with or without anyone inside.
    /// </summary>
    private void UpdateAutoLaunch(Entity<DropPodComponent> pod, TimeSpan now)
    {
        if (pod.Comp.AutoLaunchAt is not { } autoLaunchAt)
            return;

        if (now >= autoLaunchAt)
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"Extraction drop pod {ToPrettyString(pod.Owner)} left the station on its own");
            LaunchPod(pod);
            return;
        }

        if (pod.Comp.AutoLaunchWarned
            || autoLaunchAt - now > pod.Comp.AutoLaunchWarning
            || !TryComp<AntagExtractableComponent>(pod.Comp.Extractee, out var extractable))
            return;

        pod.Comp.AutoLaunchWarned = true;
        Notify((pod.Comp.Extractee.Value, extractable),
            Loc.GetString(extractable.Messages.PodLeavingSoon, ("seconds", (int) Math.Ceiling((autoLaunchAt - now).TotalSeconds))));
    }

    private void LaunchPod(Entity<DropPodComponent> pod)
    {
        var coords = Transform(pod).Coordinates;
        StopLaunch(pod);
        pod.Comp.AutoLaunchAt = null;

        if (pod.Comp.Extractee is { } extractee
            && TryComp<AntagExtractableComponent>(extractee, out var extractable))
        {
            extractable.Pod = null;

            if (_container.TryGetContainingContainer(extractee, out var container) && container.Owner == pod.Owner)
                Extract((extractee, extractable), coords);
            else
                Notify((extractee, extractable), Loc.GetString(extractable.Messages.PodLeftWithout));
        }

        // anyone and anything else stuffed inside stays on the station
        _entityStorage.EmptyContents(pod.Owner);

        RaiseNetworkEvent(new DropPodLaunchAnimationEvent(GetNetCoordinates(coords), pod.Comp.LaunchAnimationDuration, pod.Comp.LaunchSprite, pod.Comp.LaunchSpriteOffset),
            Filter.Pvs(pod.Owner));
        _audio.PlayPvs(pod.Comp.LaunchSound, coords);

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Extraction drop pod {ToPrettyString(pod.Owner)} launched from {coords}");
        QueueDel(pod.Owner);
    }

    #endregion
}
