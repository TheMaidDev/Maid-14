using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Paper;
using Content.Server.Station.Components;
using Content.Shared._Maid.TradeShuttleConsole;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Labels.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Paper;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Maid.TradeShuttleConsole;

// That shit mostly copied from cargo system

public sealed partial class TradeShuttleConsoleSystem
{
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PaperSystem _paperSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private EntityQuery<TransformComponent> transformQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<CargoSellBlacklistComponent> _blacklistQuery;

    private readonly HashSet<EntityUid> _setEnts = [];
    private readonly List<(EntityUid, CargoPalletComponent, TransformComponent)> _pads = [];

    private void InitializeOrders()
    {
        transformQuery = GetEntityQuery<TransformComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _blacklistQuery = GetEntityQuery<CargoSellBlacklistComponent>();

        SubscribeLocalEvent<FulfillCargoOrderEvent>(OnFulfillCargoOrder);
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

    private void SellSoldThings(Entity<TradeShuttleComponent> ent)
    {
        if (!TryComp<TradeMapComponent>(Transform(ent).MapUid, out var tradeMap) || !tradeMap.AttachedStation.IsValid())
            return;

        SellCargo(ent.Owner, tradeMap.AttachedStation);
    }

    private void SellCargo(EntityUid gridUid, EntityUid station)
    {
        if (!TryComp<StationBankAccountComponent>(station, out var bankAccount))
            return;

        if (!SellPallets(gridUid, station, out var goods))
            return;

        var baseDistribution = _cargoSystem.CreateAccountDistribution((station, bankAccount));
        var lockboxCutEnabled = _configurationManager.GetCVar(CCVars.LockboxCutEnabled);

        foreach (var (_, sellComponent, value) in goods)
        {
            Dictionary<ProtoId<CargoAccountPrototype>, double> distribution;
            if (sellComponent != null)
            {
                var cut = lockboxCutEnabled ? bankAccount.LockboxCut : bankAccount.PrimaryCut;
                distribution = new Dictionary<ProtoId<CargoAccountPrototype>, double>
                {
                    { sellComponent.OverrideAccount, cut },
                    { bankAccount.PrimaryAccount, 1.0 - cut },
                };
            }
            else
            {
                distribution = baseDistribution;
            }

            _cargoSystem.UpdateBankAccount((station, bankAccount), (int) Math.Round(value), distribution, false);
        }

        Dirty(station, bankAccount);
    }

    private bool SellPallets(EntityUid gridUid, EntityUid station, out HashSet<(EntityUid, OverrideSellComponent?, double)> goods)
    {
        GetPalletGoods(gridUid, out var toSell, out goods);

        if (toSell.Count == 0)
            return false;

        var ev = new EntitySoldEvent(toSell, station);
        RaiseLocalEvent(ref ev);

        foreach (var ent in toSell)
        {
            Del(ent);
        }

        return true;
    }

    private void GetPalletGoods(EntityUid gridUid, out HashSet<EntityUid> toSell, out HashSet<(EntityUid, OverrideSellComponent?, double)> goods)
    {
        goods = new HashSet<(EntityUid, OverrideSellComponent?, double)>();
        toSell = new HashSet<EntityUid>();
        foreach (var (palletUid, _, _) in GetCargoPallets(gridUid, BuySellType.All))
        {
            _setEnts.Clear();

            _lookup.GetEntitiesIntersecting(
                palletUid,
                _setEnts,
                LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var ent in _setEnts)
            {
                if (toSell.Contains(ent) ||
                    transformQuery.TryGetComponent(ent, out var xform) &&
                    (xform.Anchored || !CanSell(ent, xform)))
                {
                    continue;
                }

                if (_blacklistQuery.HasComponent(ent))
                    continue;

                var price = _pricing.GetPrice(ent);
                if (price == 0)
                    continue;
                toSell.Add(ent);
                goods.Add((ent, CompOrNull<OverrideSellComponent>(ent), price));
            }
        }
    }

    private bool CanSell(EntityUid uid, TransformComponent xform)
    {
        if (_mobQuery.HasComponent(uid))
        {
            return false;
        }

        var complete = _cargoSystem.IsBountyComplete(uid, out var bountyEntities);

        // Recursively check for mobs at any point.
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (complete && bountyEntities.Contains(child))
                continue;

            if (!CanSell(child, transformQuery.GetComponent(child)))
                return false;
        }

        return true;
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
        var buyPallets = GetCargoPallets(ent.Owner, BuySellType.All);
        _random.Shuffle(buyPallets);

        var freePallets = GetFreeCargoPallets(ent.Owner, buyPallets);

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
                if (FulfillOrder(orderData, orderData.Account, coords, printerOutput))
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

    private List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent PalletXform)> GetCargoPallets(EntityUid gridUid, BuySellType requestType = BuySellType.All)
    {
        _pads.Clear();

        var query = AllEntityQuery<CargoPalletComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var compXform))
        {
            if (compXform.ParentUid != gridUid ||
                !compXform.Anchored)
                continue;

            if ((requestType & comp.PalletType) == 0)
                continue;

            _pads.Add((uid, comp, compXform));
        }

        return _pads;
    }

    private List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)>
        GetFreeCargoPallets(EntityUid gridUid,
            List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)> pallets)
    {
        var outList = new List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)>();

        foreach (var pallet in pallets)
        {
            var aabb = _lookup.GetAABBNoContainer(pallet.Entity, pallet.Transform.LocalPosition, pallet.Transform.LocalRotation);

            if (_lookup.AnyLocalEntitiesIntersecting(gridUid, aabb, LookupFlags.Dynamic))
                continue;

            outList.Add(pallet);
        }

        return outList;
    }

    private bool FulfillOrder(CargoOrderData order, ProtoId<CargoAccountPrototype> account, EntityCoordinates spawn, string? paperProto)
    {
        // Create the item itself
        var item = Spawn(order.ProductId, spawn);

        // Ensure the item doesn't start anchored
        _transform.Unanchor(item, Transform(item));

        // Create a sheet of paper to write the order details on
        var printed = Spawn(paperProto, spawn);
        if (TryComp<PaperComponent>(printed, out var paper))
        {
            // fill in the order data
            var val = Loc.GetString("cargo-console-paper-print-name", ("orderNumber", order.OrderId));
            _metaData.SetEntityName(printed, val);

            var accountProto = _prototypeManager.Index(account);
            _paperSystem.SetContent((printed, paper),
                Loc.GetString(
                    "cargo-console-paper-print-text",
                    ("orderNumber", order.OrderId),
                    ("itemName", MetaData(item).EntityName),
                    ("orderQuantity", order.OrderQuantity),
                    ("requester", order.Requester),
                    ("reason", string.IsNullOrWhiteSpace(order.Reason) ? Loc.GetString("cargo-console-paper-reason-default") : order.Reason),
                    ("account", Loc.GetString(accountProto.Name)),
                    ("accountcode", Loc.GetString(accountProto.Code)),
                    ("approver", string.IsNullOrWhiteSpace(order.Approver) ? Loc.GetString("cargo-console-paper-approver-default") : order.Approver)));

            // attempt to attach the label to the item
            if (TryComp<PaperLabelComponent>(item, out var label))
            {
                _itemSlots.TryInsert(item, label.LabelSlot, printed, null);
            }
        }

        return true;
    }
}
