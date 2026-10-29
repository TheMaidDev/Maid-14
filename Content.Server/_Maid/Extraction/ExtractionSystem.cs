using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Antag;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Decals;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Objectives.Components;
using Content.Server.Pinpointer;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._DV.Carrying;
using Content.Shared._Maid.Extraction;
using Content.Shared.Actions;
using Content.Shared.Camera;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.Eye;
using Content.Shared.Follower;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Follower.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Objectives.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Unlocks extraction once objectives are done and places the extraction zone at a station holopad on request.
/// The pod part lives in ExtractionSystem.Pod.cs.
/// Extracted bodies are kept alive on a paused map so the character counts as alive, not dead.
/// </summary>
public sealed partial class ExtractionSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly CarryingSystem _carrying = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly SharedDestructibleSystem _destructible = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly FollowerSystem _follower = default!;
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NavMapSystem _navMap = default!;
    [Dependency] private readonly SharedObjectivesSystem _objectives = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedProjectileSystem _projectile = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ExtractionZoneSystem _zone = default!;

    /// <summary>
    /// Paused map holding the bodies of extracted antagonists.
    /// </summary>
    private EntityUid? _limboMap;

    private readonly List<(string Name, int Minutes, LocId Message)> _extracted = new();

    /// <summary>
    /// Extracted bodies whose player came back into them, e.g. after a reconnect deleted their ghost.
    /// </summary>
    private readonly HashSet<EntityUid> _reghost = new();

    /// <summary>
    /// Pods landing this tick, handled after the query because landing can gib the extractee.
    /// </summary>
    private readonly List<(EntityUid Uid, EntityCoordinates Target)> _landing = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AfterAntagEntitySelectedEvent>(OnAntagSelected);
        SubscribeLocalEvent<GetVisMaskEvent>(OnGetVisMask);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<GhostComponent, PlayerAttachedEvent>(OnGhostAttached);

        SubscribeLocalEvent<AntagExtractableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AntagExtractableComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<AntagExtractableComponent, PlayerAttachedEvent>(OnPlayerAttached);

        InitializePod();
        InitializeTest();
        InitializeDevice();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        foreach (var body in _reghost)
        {
            Reghost(body);
        }

        _reghost.Clear();

        UpdatePendingDeviceUis(now);

        var query = EntityQueryEnumerator<AntagExtractableComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Extracted)
                continue;

            if (!comp.DeviceGiven)
                GiveDevice((uid, comp));

            // all zones got destroyed (singularity, grid deletion...), a new set is given for free
            if (PruneZones((uid, comp)))
                ReplaceLostZones((uid, comp));

            if (UpdatePodCall((uid, comp), now) is { } target)
                _landing.Add((uid, target));

            if (comp.Unlocked || now < comp.NextCheck)
                continue;

            comp.NextCheck = now + comp.CheckInterval;

            if (!_mind.TryGetMind(uid, out var mindId, out var mind)
                || !ObjectivesMet((uid, comp), (mindId, mind), out var completed, out var total))
                continue;

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(uid):player} unlocked extraction with {completed}/{total} counted objectives completed");
            Unlock((uid, comp));
        }

        foreach (var (uid, target) in _landing)
        {
            if (TryComp<AntagExtractableComponent>(uid, out var comp))
                LandPod((uid, comp), target);
        }

        _landing.Clear();

        UpdatePods(now);
        UpdateLandingTests(now);
    }

    #region Gear

    private void OnMapInit(Entity<AntagExtractableComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ArrivalTime = _timing.CurTime;
    }

    private void OnAntagSelected(ref AfterAntagEntitySelectedEvent args)
    {
        if (!TryComp<AntagExtractableComponent>(args.EntityUid, out var comp))
            return;

        comp.ArrivalTime = _timing.CurTime;
        MarkCarriedGear(args.EntityUid);
    }

    private void MarkCarriedGear(EntityUid uid)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(uid))
        {
            MarkGear(item);
        }
    }

    private void MarkGear(EntityUid item)
    {
        if (HasComp<VirtualItemComponent>(item))
            return;

        EnsureComp<ExtractionGearComponent>(item);

        foreach (var contained in GetContained(item))
        {
            MarkGear(contained);
        }
    }

    /// <summary>
    /// Drops every station item found on or inside <paramref name="item"/> at the given coordinates.
    /// Gear stays where it is and leaves together with the extractee.
    /// </summary>
    private void ReleaseStationItems(EntityUid user, EntityUid item, EntityCoordinates coords, List<EntityUid> released)
    {
        // something released earlier may have pulled this one along, e.g. a hardsuit retracting its helmet
        if (HasComp<VirtualItemComponent>(item) || !IsCarriedBy(item, user))
            return;

        if (HasComp<ExtractionGearComponent>(item))
        {
            foreach (var contained in GetContained(item))
            {
                ReleaseStationItems(user, contained, coords, released);
            }

            return;
        }

        // actions, solutions and other non-item children stay where they are
        if (!HasComp<ItemComponent>(item))
            return;

        if (_container.TryGetContainingContainer(item, out var container))
            _container.Remove(item, container, force: true, destination: coords);

        DeleteNestedGear(item);
        released.Add(item);
    }

    /// <summary>
    /// Drops what is attached to the body outside of inventory: carried people always,
    /// embedded projectiles and contents of storage implants when station items are left behind.
    /// </summary>
    private void ReleaseAttached(EntityUid user, EntityCoordinates coords, List<EntityUid> released, bool releaseItems)
    {
        if (TryComp<CarryingComponent>(user, out var carrying))
        {
            var carried = carrying.Carried;
            _carrying.DropCarried(user, carried);
            _transform.SetCoordinates(carried, coords);
            released.Add(carried);
        }

        if (TryComp<FollowedComponent>(user, out var followed))
            _follower.StopAllFollowers(user, followed);

        var children = new List<EntityUid>();
        var enumerator = Transform(user).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            children.Add(child);
        }

        foreach (var child in children)
        {
            if (TerminatingOrDeleted(child)
                || _container.IsEntityInContainer(child)
                || HasComp<ExtractionGearComponent>(child))
                continue;

            var isPerson = HasComp<MindContainerComponent>(child);
            if (!isPerson && !releaseItems)
                continue;

            if (TryComp<EmbeddableProjectileComponent>(child, out var embed))
            {
                _projectile.EmbedDetach(child, embed);
                if (TerminatingOrDeleted(child) || EntityManager.IsQueuedForDeletion(child))
                    continue;
            }
            else if (!isPerson && !HasComp<ItemComponent>(child))
            {
                continue;
            }

            _transform.SetCoordinates(child, coords);
            released.Add(child);
        }

        if (releaseItems && TryComp<ImplantedComponent>(user, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities.ToList())
            {
                foreach (var contained in GetContained(implant))
                {
                    ReleaseStationItems(user, contained, coords, released);
                }
            }
        }
    }

    private bool IsCarriedBy(EntityUid item, EntityUid user)
    {
        var parent = Transform(item).ParentUid;
        while (parent.IsValid())
        {
            if (parent == user)
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }

    private void DeleteNestedGear(EntityUid item)
    {
        foreach (var contained in GetContained(item))
        {
            if (HasComp<ExtractionGearComponent>(contained))
                QueueDel(contained);
            else
                DeleteNestedGear(contained);
        }
    }

    private List<EntityUid> GetContained(EntityUid uid)
    {
        var result = new List<EntityUid>();
        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return result;

        foreach (var container in _container.GetAllContainers(uid, manager))
        {
            result.AddRange(container.ContainedEntities);
        }

        return result;
    }

    #endregion

    #region Objectives

    /// <summary>
    /// Checks required objectives and the threshold over required and optional ones.
    /// Objectives in neither list are not counted. With both lists empty every objective
    /// counts except survival ones. Without any counted objective extraction stays locked.
    /// </summary>
    public bool ObjectivesMet(Entity<AntagExtractableComponent> ent, Entity<MindComponent> mind, out int completed, out int total)
    {
        var counted = GetCountedObjectives(ent, mind);
        total = counted.Count;
        completed = counted.Count(o => o.Completed);

        if (total == 0 || counted.Any(o => o.Required && !o.Completed))
            return false;

        return ent.Comp.ThresholdMode switch
        {
            ExtractionThresholdMode.Count => completed >= Math.Min(ent.Comp.Threshold, total),
            _ => completed * 100f / total >= ent.Comp.Threshold - 0.01f,
        };
    }

    /// <summary>
    /// Objectives of the mind that count for extraction, with their state.
    /// </summary>
    public List<(EntityUid Objective, bool Completed, bool Required)> GetCountedObjectives(
        Entity<AntagExtractableComponent> ent,
        Entity<MindComponent> mind)
    {
        var result = new List<(EntityUid, bool, bool)>();
        var countAll = ent.Comp.RequiredObjectives.Count == 0 && ent.Comp.OptionalObjectives.Count == 0;

        foreach (var objective in mind.Comp.Objectives)
        {
            if (MetaData(objective).EntityPrototype?.ID is not { } id)
                continue;

            var required = ent.Comp.RequiredObjectives.Contains(id);
            if (countAll ? HasComp<SurviveConditionComponent>(objective) : !required && !ent.Comp.OptionalObjectives.Contains(id))
                continue;

            result.Add((objective, _objectives.IsCompleted(objective, mind), required));
        }

        return result;
    }

    private void Unlock(Entity<AntagExtractableComponent> ent, bool forced = false)
    {
        ent.Comp.Unlocked = true;
        ent.Comp.ForceUnlocked |= forced;

        if (string.IsNullOrEmpty(ent.Comp.Code))
        {
            var length = Math.Clamp(ent.Comp.CodeLength, 1, 9);
            ent.Comp.CodeLength = length;
            ent.Comp.Code = _random.Next((int) Math.Pow(10, length)).ToString().PadLeft(length, '0');
        }

        Dirty(ent);

        Notify(ent, Loc.GetString(forced ? ent.Comp.Messages.UnlockedForced : ent.Comp.Messages.Unlocked, ("code", ent.Comp.Code)));
    }

    /// <summary>
    /// Admin command: unlocks extraction regardless of objectives.
    /// Entities without <see cref="AntagExtractableComponent"/> get it, with what they carry now counted as their gear.
    /// </summary>
    public string UnlockCommand(EntityUid uid)
    {
        if (!Exists(uid))
            return Loc.GetString("extraction-command-no-entity");

        var name = ToPrettyString(uid).ToString();
        var added = false;
        if (!TryComp<AntagExtractableComponent>(uid, out var comp))
        {
            comp = AddComp<AntagExtractableComponent>(uid);
            MarkCarriedGear(uid);
            added = true;
        }

        if (comp.Unlocked)
            return Loc.GetString("extraction-command-already-unlocked", ("target", name));

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Extraction of {ToPrettyString(uid):player} was unlocked by an admin command");
        Unlock((uid, comp), true);
        return Loc.GetString(added ? "extraction-command-unlocked-added" : "extraction-command-unlocked", ("target", name));
    }

    #endregion

    #region Zone

    /// <summary>
    /// Designates the extraction zones, or moves all of them while re-requests are left.
    /// </summary>
    public void TryRequestZone(Entity<AntagExtractableComponent> ent)
    {
        if (!ent.Comp.Unlocked || ent.Comp.Extracted)
            return;

        PruneZones(ent);
        var current = ent.Comp.Zones.ToList();
        if (ent.Comp.RequestsUsed >= ent.Comp.MaxRequests)
        {
            Notify(ent, current.Count > 0
                ? Loc.GetString(ent.Comp.Messages.NoRequestsZone, ("location", GetZonesLocation(ent)))
                : Loc.GetString(ent.Comp.Messages.NoRequests));
            return;
        }

        // a re-request moves the zones away from the current anchors when possible
        var exclude = current
            .Select(z => CompOrNull<ExtractionZoneComponent>(z)?.Anchor)
            .OfType<EntityUid>()
            .ToHashSet();

        if (!SpawnZones(ent, exclude))
        {
            Notify(ent, Loc.GetString(ent.Comp.Messages.NoZone));
            return;
        }

        foreach (var old in current)
        {
            QueueDel(old);
        }

        ent.Comp.RequestsUsed++;
        Dirty(ent);

        var left = ent.Comp.MaxRequests - ent.Comp.RequestsUsed;
        Notify(ent, Loc.GetString(ent.Comp.Messages.ZoneDesignated, ("location", GetZonesLocation(ent)), ("left", left)));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(ent.Owner):player} requested extraction zones at {GetZonesLocation(ent)} ({ent.Comp.RequestsUsed}/{ent.Comp.MaxRequests})");
    }

    /// <summary>
    /// Forgets destroyed zones. Returns true when there were zones and none is left.
    /// </summary>
    private bool PruneZones(Entity<AntagExtractableComponent> ent)
    {
        if (ent.Comp.Zones.Count == 0)
            return false;

        if (ent.Comp.Zones.RemoveAll(z => TerminatingOrDeleted(z)) == 0)
            return false;

        Dirty(ent);
        return ent.Comp.Zones.Count == 0;
    }

    /// <summary>
    /// Losing some zones changes nothing; once all of them are destroyed a new set is given for free.
    /// </summary>
    private void ReplaceLostZones(Entity<AntagExtractableComponent> ent)
    {
        if (!SpawnZones(ent, new HashSet<EntityUid>()))
        {
            Notify(ent, Loc.GetString(ent.Comp.Messages.ZoneLostNone));
            _adminLog.Add(LogType.Action, LogImpact.Low, $"All extraction zones of {ToPrettyString(ent.Owner):player} were destroyed, no replacement found");
            return;
        }

        Notify(ent, Loc.GetString(ent.Comp.Messages.ZoneLost, ("location", GetZonesLocation(ent))));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"All extraction zones of {ToPrettyString(ent.Owner):player} were destroyed, replaced at {GetZonesLocation(ent)}");
    }

    /// <summary>
    /// Main grids of the stations events can target, so no CentComm or other off-station bases.
    /// The main grid of a station is its largest member grid by bounding box (SharedStationSystem.GetLargestGrid).
    /// </summary>
    private HashSet<EntityUid> GetStationGrids()
    {
        var grids = new HashSet<EntityUid>();
        var stations = EntityQueryEnumerator<StationEventEligibleComponent>();
        while (stations.MoveNext(out var station, out _))
        {
            if (_station.GetLargestGrid(station) is { } grid)
                grids.Add(grid);
        }

        return grids;
    }

    /// <summary>
    /// Places <see cref="AntagExtractableComponent.ZoneCount"/> zones on the station's main grid, replacing the current list.
    /// Anchors are taken by kind in order of preference (holopads, then beacons), random tiles fill whatever is missing.
    /// Only the extractee and admin ghosts receive the zones.
    /// </summary>
    private bool SpawnZones(Entity<AntagExtractableComponent> ent, HashSet<EntityUid> exclude)
    {
        var stationGrids = GetStationGrids();
        var picked = new List<EntityUid>();

        // first try fresh anchors, then allow the excluded ones rather than falling back to random tiles
        foreach (var allowExcluded in new[] { false, true })
        {
            foreach (var componentName in ent.Comp.ZoneAnchorComponents)
            {
                if (picked.Count >= ent.Comp.ZoneCount)
                    break;

                if (!_componentFactory.TryGetRegistration(componentName, out var registration))
                {
                    Log.Error($"Unknown extraction zone anchor component {componentName} on {ToPrettyString(ent.Owner)}");
                    continue;
                }

                var candidates = new List<EntityUid>();
                foreach (var (uid, _) in EntityManager.GetAllComponents(registration.Type))
                {
                    if (picked.Contains(uid) || !allowExcluded && exclude.Contains(uid))
                        continue;

                    var xform = Transform(uid);
                    if (!xform.Anchored || xform.GridUid is not { } grid || !stationGrids.Contains(grid))
                        continue;

                    if (MetaData(uid).EntityPrototype?.ID is { } protoId && ent.Comp.ZoneAnchorBlacklist.Contains(protoId))
                        continue;

                    candidates.Add(uid);
                }

                _random.Shuffle(candidates);
                picked.AddRange(candidates.Take(ent.Comp.ZoneCount - picked.Count));
            }
        }

        var positions = new List<(EntityUid? Anchor, EntityCoordinates Position)>();
        foreach (var anchor in picked)
        {
            positions.Add((anchor, Transform(anchor).Coordinates));
        }

        // no anchors left on the station: random spots inside it, the player gets exact coordinates
        while (positions.Count < ent.Comp.ZoneCount && TryPickRandomTile(stationGrids, out var tile))
        {
            positions.Add((null, tile));
        }

        if (positions.Count == 0)
            return false;

        ent.Comp.Zones.Clear();
        foreach (var (anchor, position) in positions)
        {
            var zone = Spawn(ent.Comp.ZonePrototype, position);
            var zoneComp = EnsureComp<ExtractionZoneComponent>(zone);
            zoneComp.Extractee = ent.Owner;
            zoneComp.Anchor = anchor;
            zoneComp.ShowCoordinates = anchor == null;
            Dirty(zone, zoneComp);

            _metaData.SetEntityDescription(zone, Loc.GetString(ent.Comp.Messages.ZoneMarkerDescription, ("name", Name(ent.Owner))));
            ent.Comp.Zones.Add(zone);
        }

        Dirty(ent);
        SendZonesToOwner(ent);
        return true;
    }

    /// <summary>
    /// How many random tiles are tried when looking for a zone spot on a station without anchors.
    /// </summary>
    private const int RandomTileAttempts = 200;

    /// <summary>
    /// Air pressure a tile needs to count as inside the station, so the zone never ends up on outer lattice.
    /// </summary>
    private const float RandomTileMinPressure = 50f;

    /// <summary>
    /// A random free, pressurised floor tile on one of the station grids, for stations without zone anchors.
    /// Only a bounded number of random tiles is checked so big stations do not stall the tick.
    /// </summary>
    private bool TryPickRandomTile(HashSet<EntityUid> grids, out EntityCoordinates coords)
    {
        coords = default;
        var tiles = new List<(EntityUid Grid, Vector2i Tile)>();

        foreach (var grid in grids)
        {
            if (!TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            foreach (var tileRef in _map.GetAllTiles(grid, gridComp))
            {
                tiles.Add((grid, tileRef.GridIndices));
            }
        }

        for (var i = 0; i < RandomTileAttempts && tiles.Count > 0; i++)
        {
            var index = _random.Next(tiles.Count);
            var (grid, tile) = tiles[index];
            tiles[index] = tiles[^1];
            tiles.RemoveAt(tiles.Count - 1);

            var gridComp = Comp<MapGridComponent>(grid);
            if (_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp))
                continue;

            if (_atmosphere.GetTileMixture(grid, null, tile) is not { } air || air.Pressure < RandomTileMinPressure)
                continue;

            coords = _map.GridTileToLocal(grid, gridComp, tile);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Where one zone is, for messages: the nearest beacon, plus exact coordinates for a zone at a random tile.
    /// </summary>
    private string GetZoneLocation(Entity<AntagExtractableComponent> ent, EntityUid zone)
    {
        var location = GetLocation(zone);
        if (!TryComp<ExtractionZoneComponent>(zone, out var zoneComp) || !zoneComp.ShowCoordinates)
            return location;

        var pos = _transform.GetMapCoordinates(zone).Position;
        return Loc.GetString(ent.Comp.Messages.ZoneCoordinates,
            ("location", location),
            ("x", ((int) MathF.Round(pos.X)).ToString()),
            ("y", ((int) MathF.Round(pos.Y)).ToString()));
    }

    /// <summary>
    /// All remaining zones, one per line.
    /// </summary>
    public string GetZonesLocation(Entity<AntagExtractableComponent> ent)
    {
        return string.Join("\n", ent.Comp.Zones
            .Where(z => !TerminatingOrDeleted(z))
            .Select(z => "— " + GetZoneLocation(ent, z)));
    }

    private void SendZonesToOwner(Entity<AntagExtractableComponent> ent)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        foreach (var zone in ent.Comp.Zones)
        {
            if (!TerminatingOrDeleted(zone))
                _pvsOverride.AddForceSend(zone, actor.PlayerSession);
        }
    }

    private void OnGetVisMask(ref GetVisMaskEvent args)
    {
        // admin ghosts see every extraction zone, everyone else only gets their own one force-sent
        if (HasComp<GhostComponent>(args.Entity)
            && TryComp<ActorComponent>(args.Entity, out var actor)
            && _admin.IsAdmin(actor.PlayerSession))
            args.VisibilityMask |= (int) VisibilityFlags.MaidExtraction;
    }

    private void OnGhostAttached(Entity<GhostComponent> ent, ref PlayerAttachedEvent args)
    {
        // admin ghosts are spawned before the player is attached, so the mask has to be refreshed now
        _eye.RefreshVisibilityMask(ent.Owner);
    }

    private void OnPlayerAttached(Entity<AntagExtractableComponent> ent, ref PlayerAttachedEvent args)
    {
        if (ent.Comp.Extracted)
        {
            _reghost.Add(ent.Owner);
            return;
        }

        if (!ent.Comp.Unlocked)
            return;

        // force-sends are dropped on disconnect, and the player may have missed the messages
        SendZonesToOwner(ent);
        PruneZones(ent);
        Notify(ent, ent.Comp.Zones.Count > 0
            ? Loc.GetString(ent.Comp.Messages.ReminderZone, ("location", GetZonesLocation(ent)), ("code", ent.Comp.Code))
            : Loc.GetString(ent.Comp.Messages.Reminder, ("code", ent.Comp.Code)));
    }

    private string GetLocation(EntityUid uid)
    {
        return FormattedMessage.RemoveMarkupPermissive(_navMap.GetNearestBeaconString((uid, null)));
    }

    private string GetLocation(EntityCoordinates coords)
    {
        return FormattedMessage.RemoveMarkupPermissive(_navMap.GetNearestBeaconString(_transform.ToMapCoordinates(coords)));
    }

    /// <summary>
    /// Admin command: removes the zones and lets the extractee request them and call the pod again.
    /// </summary>
    public string ResetCommand(EntityUid uid)
    {
        if (!Exists(uid))
            return Loc.GetString("extraction-command-no-entity");

        var name = ToPrettyString(uid).ToString();
        if (!TryComp<AntagExtractableComponent>(uid, out var comp))
            return Loc.GetString("extraction-command-no-component", ("target", name));

        foreach (var zone in comp.Zones)
        {
            QueueDel(zone);
        }

        comp.Zones.Clear();
        comp.RequestsUsed = 0;
        comp.PodCalled = false;
        comp.PodTarget = null;
        comp.PodAnnounceAt = null;
        Dirty(uid, comp);

        if (comp.Unlocked)
            Notify((uid, comp), Loc.GetString(comp.Messages.ZoneReset));

        _adminLog.Add(LogType.Action, LogImpact.Low, $"Extraction zones and pod call of {ToPrettyString(uid):player} were reset by an admin command");
        return Loc.GetString("extraction-command-reset", ("target", name));
    }

    private void OnRemove(Entity<AntagExtractableComponent> ent, ref ComponentRemove args)
    {
        foreach (var zone in ent.Comp.Zones)
        {
            QueueDel(zone);
        }

        ent.Comp.Zones.Clear();
        ent.Comp.PodTarget = null;

        // the device and the pod must not keep pointing at a gone owner, they are networked
        if (ent.Comp.Device is { } device && TryComp<ExtractionDeviceComponent>(device, out var deviceComp))
        {
            deviceComp.Owner = null;
            Dirty(device, deviceComp);
        }

        if (ent.Comp.Pod is { } pod && TryComp<DropPodComponent>(pod, out var podComp) && podComp.Extractee == ent.Owner)
        {
            podComp.Extractee = null;
            Dirty(pod, podComp);
        }
    }

    #endregion

    #region Leaving

    /// <summary>
    /// Takes the extractee out of the round: station items drop at <paramref name="coords"/>,
    /// the player becomes a ghost and the body moves to the paused map.
    /// </summary>
    private void Extract(Entity<AntagExtractableComponent> ent, EntityCoordinates coords)
    {
        var user = ent.Owner;
        var released = new List<EntityUid>();

        if (ent.Comp.LeaveStationItems)
        {
            foreach (var item in _inventory.GetHandOrInventoryEntities(user).ToList())
            {
                ReleaseStationItems(user, item, coords, released);
            }
        }

        ReleaseAttached(user, coords, released, ent.Comp.LeaveStationItems);

        var minutes = (int) Math.Round((_timing.CurTime - ent.Comp.ArrivalTime).TotalMinutes);
        var characterName = Name(user);

        if (_mind.TryGetMind(user, out var mindId, out var mind))
        {
            characterName = mind.CharacterName ?? characterName;
            EnsureComp<ExtractedMindComponent>(mindId);

            // the mind keeps owning the living body, so the character is alive for kill and survive objectives
            if (_ghost.SpawnGhost((mindId, mind), coords, canReturn: true) is { } ghost)
                _ghost.SetCanReturnToBody(ghost, false);
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} was extracted from the round at {coords} after {minutes} min, left behind: {string.Join(", ", released.Select(e => ToPrettyString(e).ToString()))}");

        _extracted.Add((characterName, minutes, ent.Comp.Messages.RoundEnd));

        foreach (var zone in ent.Comp.Zones)
        {
            QueueDel(zone);
        }

        ent.Comp.Extracted = true;
        ent.Comp.Zones.Clear();
        MoveToLimbo(user);
    }

    /// <summary>
    /// Moves the body to a paused map: nothing can reach or hurt it there, and it stays alive.
    /// TODO MAID: exclude this map from antag and population counts (dynamic and adaptive game modes),
    /// extracted bodies still count as living antagonists there.
    /// </summary>
    private void MoveToLimbo(EntityUid user)
    {
        if (TryComp<PullerComponent>(user, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pulledComp))
            _pulling.TryStopPull(pulled, pulledComp, user);

        if (TryComp<PullableComponent>(user, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(user, pullable);

        if (_container.TryGetContainingContainer(user, out var container))
            _container.Remove(user, container, force: true);

        if (_limboMap == null || TerminatingOrDeleted(_limboMap.Value))
        {
            _limboMap = _map.CreateMap(out _);
            _metaData.SetEntityName(_limboMap.Value, "Extraction limbo");
            _map.SetPaused(_limboMap.Value, true);
        }

        _transform.SetCoordinates(user, new EntityCoordinates(_limboMap.Value, Vector2.Zero));
    }

    /// <summary>
    /// Sends the player of an extracted body back to a ghost, keeping the mind on the body.
    /// </summary>
    private void Reghost(EntityUid body)
    {
        if (TerminatingOrDeleted(body)
            || !_mind.TryGetMind(body, out var mindId, out var mind)
            || mind.IsVisitingEntity)
            return;

        if (_ghost.SpawnGhost((mindId, mind), null, canReturn: true) is { } ghost)
            _ghost.SetCanReturnToBody(ghost, false);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent ev)
    {
        foreach (var (name, minutes, message) in _extracted)
        {
            ev.AddLine(Loc.GetString(message, ("name", name), ("minutes", minutes)));
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _limboMap = null;
        _reghost.Clear();
        _landing.Clear();
        _extracted.Clear();
        _nextKeypadInput.Clear();
    }

    #endregion

    private void Notify(Entity<AntagExtractableComponent> ent, string message)
    {
        _popup.PopupEntity(Loc.GetString(ent.Comp.Messages.NotifyPopup), ent.Owner, ent.Owner, PopupType.Medium);

        if (TryComp<ActorComponent>(ent, out var actor))
            _chatManager.DispatchServerMessage(actor.PlayerSession, message);
    }
}
