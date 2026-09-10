using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Random;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Timing;
using Content.Shared.Popups;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._Maid.TradeShuttleConsole;
using Content.Shared.DeviceLinking;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Shuttles.Events;
using Content.Shared._Maid.CVars;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Maid.TradeShuttleConsole;

public sealed class TradeShuttleConsoleSystem : EntitySystem
{
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public const float FTLTradeRandomMagnitude = 50;
    public const float FTLStationDistance = 400f;

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
            // TODO: popup
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
            // TODO: check for sentient entities (+ not dead or invalid if has states) and cancel
        }
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

        // TODO: Fill bough things, like it works with trade station
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
}
