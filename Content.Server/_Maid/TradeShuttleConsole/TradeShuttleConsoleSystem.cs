using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Station.Events;
using Content.Shared._Maid.CVars;
using Content.Shared._Maid.TradeShuttleConsole;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
namespace Content.Server._Maid.TradeShuttleConsole;

public sealed class TradeShuttleConsoleSystem : EntitySystem
{
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly CargoSystem _cargoSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;

    public const float FTLTradeRandomMagnitude = 50;
    public const float FTLStationDistance = 200f;

    public override void Initialize()
    {
        base.Initialize();


        SubscribeLocalEvent<TradeShuttleComponent, LinkAttemptEvent>(AttemptToLink);
        SubscribeLocalEvent<TradeShuttleConsoleComponent, NewLinkEvent>(OnConsoleLinked);
        SubscribeLocalEvent<TradeShuttleConsoleComponent, PortDisconnectedEvent>(OnConsoleUnlinked);

        Subs.BuiEvents<TradeShuttleConsoleComponent>(TradeShuttleConsoleUiKey.Key,
        subs =>
        {
           subs.Event<TradeShuttleConsoleButtonPressedMessage>(OnButtonPressed);
        });

        SubscribeLocalEvent<TradeShuttleComponent, FTLCompletedEvent>(OnFTLFinish);
        SubscribeLocalEvent<TradeShuttleComponent, BeforeFTLStartedEvent>(OnBeforeFTLStarted);
        SubscribeLocalEvent<TradeShuttleComponent, FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<TradeShuttleComponent, FTLCooldownFinishEvent>(OnFTLCooldownFinish);
        SubscribeLocalEvent<FulfillCargoOrderEvent>(OnFulfillCargoOrder);
        SubscribeLocalEvent<AttachedTradeMapComponent, StationPostInitEvent>(OnStationPostInit);
    }
    private void OnStationPostInit(EntityUid uid, AttachedTradeMapComponent comp, ref StationPostInitEvent args)
    {
        EnsureTradeMap(uid);
    }


    private void AttemptToLink(Entity<TradeShuttleComponent> ent, ref LinkAttemptEvent args)
    {
        switch (args.SourcePort)
        {
            case "TradeShuttleSource":
                if (args.SinkPort == "TradeShuttleSink")
                    return;
                break;
        }
        args.Cancel();
    }

    private void OnConsoleLinked(Entity<TradeShuttleConsoleComponent> ent, ref NewLinkEvent args)
    {
        _deviceLink.RemoveAllFromSource(args.Sink); // This will hopefully avoid circular dependency

        if (!TryComp(args.Sink, out TradeShuttleConsoleComponent? sinkConsole))
            return;

        ent.Comp.LinkedToConsole = args.Sink;
        sinkConsole.LinkedFromConsole.Add(ent);
    }

    private void OnConsoleUnlinked(Entity<TradeShuttleConsoleComponent> ent, ref PortDisconnectedEvent args)
    {
        var linked = ent.Comp.LinkedToConsole;
        ent.Comp.LinkedToConsole = null;

        if (linked is not null && TryComp(linked, out TradeShuttleConsoleComponent? console))
            console.LinkedFromConsole.Remove(ent);
    }

    private EntityUid? GetStation(Entity<TradeShuttleConsoleComponent?> from, int iteration = 0)
    {
        if (iteration > 3) // Just to be safe
            return null; // One console can't have input and output at the same time tho

        if (_stationSystem.GetOwningStation(from.Owner) is { } station)
            return station;

        if (!Resolve(from.Owner, ref from.Comp))
            return null;

        // kinda scary but we SHOULDNT have circular dependencies
        return from.Comp.LinkedFromConsole.FirstOrNull(console => GetStation(console, iteration + 1) is not null);
    }

    private void Trigger(Entity<TradeShuttleConsoleComponent?> console, Entity<TradeShuttleConsoleComponent?> caller)
    {
        if (!Resolve(caller.Owner, ref caller.Comp) || !Resolve(console.Owner, ref console.Comp))
            return;

        var shuttle = Transform(console.Owner).GridUid;
        if (shuttle is null)
        {
            _popup.PopupEntity(Loc.GetString("cargo-no-shuttle"), caller.Owner);
            UpdateUI(console, new TradeShuttleConsoleErrorUIState
            {
                OnTrade = false,
                ControlledShuttle = "???",
                Error = TradeShuttleConsoleErrorUIState.ErrorType.ShuttleNotFound,
            });
            return;
        }

        var controlledShuttleName = caller.Owner == console.Owner ? null : Name(shuttle.Value);

        var station = GetStation(console.Owner);

        if (station is null)
            return; // TODO: Probably should report an error?

        var tradeMap = EnsureTradeMap(station.Value);

        if (HasComp<FTLComponent>(shuttle))
            return;

        // We are on trade
        if (Transform(shuttle.Value).MapID == tradeMap)
        {
            SendToStation(console, caller, shuttle.Value, station.Value);
            return;
        }

        // We anywhere except on station
        SendToTrade(console, caller, shuttle.Value, tradeMap);
    }

    private MapId EnsureTradeMap(EntityUid station)
    {
        var tradeMapComp = EnsureComp<AttachedTradeMapComponent>(station);
        if (tradeMapComp.AttachedMap != MapId.Nullspace)
            return tradeMapComp.AttachedMap;

        var tradeMap = EnsureComp<TradeMapComponent>(_mapSystem.CreateMap(out tradeMapComp.AttachedMap));
        tradeMap.AttachedStation = station;

        return tradeMapComp.AttachedMap;
    }

    private void SendToTrade(Entity<TradeShuttleConsoleComponent?> console,
        Entity<TradeShuttleConsoleComponent?> caller,
        Entity<ShuttleComponent?> shuttle,
        MapId tradeMap)
    {
        if (!Resolve(shuttle.Owner, ref shuttle.Comp))
            return;

        Entity<TransformComponent?> map = _mapSystem.GetMap(tradeMap);
        if (!Resolve(map.Owner, ref map.Comp))
            return;

        var comp = AddComp<TradeShuttleComponent>(shuttle);
        comp.TradeMap = tradeMap;
        comp.Console = console;

        var startupTime = _shuttleSystem.DefaultStartupTime;
        var duration = _shuttleSystem.DefaultTravelTime;

        var pos = _random.NextVector2(FTLTradeRandomMagnitude);
        _shuttleSystem.FTLToCoordinates(shuttle, shuttle.Comp, new EntityCoordinates(_mapSystem.GetMap(tradeMap), pos), 0, startupTime, duration);
        UpdateUI(console, new TradeShuttleConsoleFtlInProgressUIState
        {
            FtlState = FTLState.Starting,
            ControlledShuttle = Name(shuttle),
            FtlTime = StartEndTime.FromCurTime(_gameTiming, startupTime),
            OnTrade = false,
        });
    }

    private void SendToStation(Entity<TradeShuttleConsoleComponent?> console,
        Entity<TradeShuttleConsoleComponent?> caller,
        Entity<ShuttleComponent?> shuttle,
        Entity<AttachedTradeMapComponent?> station)
    {
        if (!Resolve(station.Owner, ref station.Comp)
            || !Resolve(shuttle.Owner, ref shuttle.Comp)
            || !Resolve(caller.Owner, ref caller.Comp)
            || !Resolve(console.Owner, ref console.Comp)
        )
            return;

        if (_stationSystem.GetLargestGrid(station.Owner) is not { } stationGrid)
            return;

        var stationTransform = Transform(stationGrid);
        if (stationTransform.MapUid is not { } mapUid)
            return;

        var tradeShuttleComponent = EnsureComp<TradeShuttleComponent>(shuttle);

        if (tradeShuttleComponent.TradeMap == MapId.Nullspace)
            tradeShuttleComponent.TradeMap = Transform(shuttle).MapID;
        tradeShuttleComponent.Console ??= console;

        var startupTime = _shuttleSystem.DefaultStartupTime;
        var duration = _shuttleSystem.DefaultTravelTime;

        var shuttleAABB = TryComp<MapGridComponent>(shuttle.Owner, out var sGrid)
            ? sGrid.LocalAABB
            : Box2.CenteredAround(Vector2.Zero, Vector2.Zero);


        var baseDistance = FTLStationDistance;

        if (TryComp<MapGridComponent>(station, out var gridComp))
        {
            var radius = MathF.Max(gridComp.LocalAABB.Width, gridComp.LocalAABB.Height) / 2f;
            baseDistance += radius;
        }

        var stationPos = _transform.GetWorldPosition(stationTransform);
        var spawnPos = stationPos + _random.NextVector2(baseDistance, baseDistance * 2); // This will be used if we will not find safe position

        const int iterationCount = 10;
        for (var i = 0; i < iterationCount; i++)
        {
            var nextBaseDistance = baseDistance + baseDistance / iterationCount; // We want it to be double at the end

            var candidatePos = stationPos + _random.NextVector2(baseDistance, nextBaseDistance);
            var candidateBox = Box2.CenteredAround(candidatePos, shuttleAABB.Size);

            var blocked = false;
            _mapManager.FindGridsIntersecting(stationTransform.MapID, candidateBox, (_, _) =>
            {
                blocked = true;
                return false;
            });

            if (!blocked)
            {
                spawnPos = candidatePos;
                break;
            }

            baseDistance = nextBaseDistance; // Try to spawn further away to increase chances
        }

        var targetCoords = new EntityCoordinates(mapUid, spawnPos);
        _shuttleSystem.FTLToCoordinates(shuttle, shuttle.Comp, targetCoords, _random.NextAngle(), startupTime, duration);

        UpdateUI(console, new TradeShuttleConsoleFtlInProgressUIState
        {
            FtlState = FTLState.Starting,
            ControlledShuttle = Name(shuttle),
            FtlTime = StartEndTime.FromCurTime(_gameTiming, startupTime),
            OnTrade = false,
        });
    }

    private void OnBeforeFTLStarted(Entity<TradeShuttleComponent> ent, ref BeforeFTLStartedEvent args)
    {
        if (_configurationManager.GetCVar(MaidCVars.DenyAliveTradeFTL))
        {
            if (ent.Comp.TradeMap == Transform(ent.Owner).MapID)
                return;

            if (!HasAliveEntities(ent.Owner))
                return;

            args.Cancelled = true;
            if (ent.Comp.Console is { } console)
            {
                _popup.PopupEntity(Loc.GetString("trade-shuttle-alive-entities-aborted"),
                    console,
                    PopupType.MediumCaution);
            }

            UpdateUI(ent.Comp.Console, new TradeShuttleConsoleIdleUIState
            {
                ControlledShuttle = Name(ent),
                OnTrade = false,
            });
        }
    }

    private bool HasAliveEntities(EntityUid uid)
    {
        var transform = Transform(uid);
        var children = transform.ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!HasComp<ActorComponent>(child))
                return false;

            if (TryComp<MobStateComponent>(child, out var mobState) && _mobStateSystem.IsDead(child, mobState))
                return true;

            if (HasAliveEntities(child))
                return true;
        }

        return false;
    }

    private void OnFTLStarted(Entity<TradeShuttleComponent> ent, ref FTLStartedEvent args)
    {
        if (ent.Comp.Console is not { } console)
            return;

        if (!TryComp(ent.Owner, out FTLComponent? ftlComponent))
            return;

        UpdateUI(console, new TradeShuttleConsoleFtlInProgressUIState()
        {
            FtlTime = ftlComponent.StateTime,
            FtlState = ftlComponent.State,
            ControlledShuttle = Name(ent.Owner),
            OnTrade = false,
        });
    }

    private void OnFTLFinish(Entity<TradeShuttleComponent> ent, ref FTLCompletedEvent args)
    {
        if (HasComp<TradeMapComponent>(args.MapUid)) // We FTLed onto trade
            OnFTLOntoTrade(ent);
        else // We FTL-ed onto station
            OnFTLOntoStation(ent);
    }

    private void OnFTLOntoStation(Entity<TradeShuttleComponent> ent)
    {
        if (!TryComp(ent.Owner, out FTLComponent? ftl))
            return;

        UpdateUI(ent.Comp.Console, new TradeShuttleConsoleFtlInProgressUIState
        {
            FtlState = ftl.State,
            FtlTime = ftl.StateTime,
            ControlledShuttle = Name(ent.Owner),
            OnTrade = false,
        });
    }

    private void OnFTLCooldownFinish(Entity<TradeShuttleComponent> ent, ref FTLCooldownFinishEvent args)
    {
        var isTradeMap = ent.Comp.TradeMap == Transform(ent).MapID;

        UpdateUI(ent.Comp.Console, new TradeShuttleConsoleIdleUIState
        {
            ControlledShuttle = Name(ent),
            OnTrade = isTradeMap,
        });

        if (!isTradeMap)
            RemComp<TradeShuttleComponent>(ent.Owner);
    }

    private void OnFTLOntoTrade(Entity<TradeShuttleComponent> ent)
    {
        if (!TryComp(ent.Owner, out FTLComponent? ftl))
            return;

        UpdateUI(ent.Comp.Console, new TradeShuttleConsoleFtlInProgressUIState()
        {
            FtlState = ftl.State,
            FtlTime = ftl.StateTime,
            ControlledShuttle = Name(ent.Owner),
            OnTrade = true,
        });

        FillBoughtThings(ent);
    }

    private void UpdateUI(Entity<TradeShuttleConsoleComponent?>? maybeConsole, TradeShuttleConsoleUIState state)
    {
        if (maybeConsole is not {} console)
            return;

        if (!Resolve(console.Owner, ref console.Comp))
            return;

        if (console.Comp.LinkedToConsole is {} linked)
        {
            console = linked;
            if (!Resolve(console.Owner, ref console.Comp))
                return;
        }

        _uiSystem.SetUiState(
            console.Owner,
            TradeShuttleConsoleUiKey.Key,
            state
        );

        foreach (var linkedFrom in console.Comp.LinkedFromConsole)
        {
            _uiSystem.SetUiState(
                linkedFrom,
                TradeShuttleConsoleUiKey.Key,
                state
            );
        }
    }

    private void OnButtonPressed(Entity<TradeShuttleConsoleComponent> ent,
        ref TradeShuttleConsoleButtonPressedMessage args)
    {
        Trigger(ent.Comp.LinkedToConsole ?? ent, ent.Owner);
    }

    private void OnFulfillCargoOrder(ref FulfillCargoOrderEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<AttachedTradeMapComponent>(args.Station, out var tradeMapComp))
            return;

        tradeMapComp.ApprovedOrders.Add(args.Order);
        args.Handled = true;
        args.FulfillmentEntity = args.Station;
    }

    private void FillBoughtThings(Entity<TradeShuttleComponent> ent)
    {
        if (!TryComp<TradeMapComponent>(Transform(ent).MapUid, out var tradeMap) || !tradeMap.AttachedStation.IsValid())
            return;

        var station = tradeMap.AttachedStation;
        if (!TryComp<AttachedTradeMapComponent>(station, out var attachedTradeMap) || attachedTradeMap.ApprovedOrders.Count == 0)
            return;

        EntProtoId printerOutput = "PaperCargoInvoice";
        if (TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDb))
            printerOutput = orderDb.PrinterOutput;

        var buyPallets = _cargoSystem.GetCargoPallets(ent.Owner, BuySellType.Buy);
        _random.Shuffle(buyPallets);

        var freePallets = _cargoSystem.GetFreeCargoPallets(ent.Owner, buyPallets);

        if (freePallets.Count == 0)
            return;

        var palletIndex = 0;
        var fulfilledCount = 0;

        foreach (var orderData in attachedTradeMap.ApprovedOrders)
        {
            if (palletIndex >= freePallets.Count)
                break;

            while (orderData.NumDispatched < orderData.OrderQuantity && palletIndex < freePallets.Count)
            {
                var pad = freePallets[palletIndex++];
                var coords = new EntityCoordinates(ent.Owner, pad.Transform.LocalPosition);
                if (_cargoSystem.FulfillOrder(orderData, orderData.Account, coords, printerOutput))
                {
                    orderData.NumDispatched++;
                }
            }

            if (orderData.NumDispatched < orderData.OrderQuantity)
                break;

            fulfilledCount++;
        }

        if (fulfilledCount > 0)
        {
            attachedTradeMap.ApprovedOrders.RemoveRange(0, fulfilledCount);
        }
    }

}
