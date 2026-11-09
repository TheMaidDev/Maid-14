using System.Linq;
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Maid.DocumentPrinter;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.DocumentPrinter;

public sealed class DocumentPrinterSystem : EntitySystem
{
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DocumentPrinterComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<DocumentPrinterComponent, DocumentPrinterPrintMessage>(OnPrint);
        SubscribeLocalEvent<EmaggedComponent, ComponentStartup>(OnEmaggedAdded);
    }

    private void OnUiOpened(Entity<DocumentPrinterComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnEmaggedAdded(Entity<EmaggedComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<DocumentPrinterComponent>(ent, out var printer))
            UpdateUi((ent, printer));
    }

    private IEnumerable<DocumentSection> AvailableSections(Entity<DocumentPrinterComponent> ent)
    {
        foreach (var section in ent.Comp.Sections)
        {
            yield return section;
        }

        if (!_emag.CheckFlag(ent, EmagType.Interaction))
            yield break;

        foreach (var section in ent.Comp.EmagSections)
        {
            yield return section;
        }
    }

    private void UpdateUi(Entity<DocumentPrinterComponent> ent)
    {
        var sections = AvailableSections(ent).ToList();
        var products = new List<ProtoId<CargoProductPrototype>>();

        if (TryComp<CargoOrderConsoleComponent>(ent, out var console))
            products = _cargo.GetAvailableProducts((ent, console));

        _ui.SetUiState(ent.Owner, DocumentPrinterUiKey.Key, new DocumentPrinterBuiState
        {
            Products = products,
            Sections = sections,
        });
    }

    private void OnPrint(EntityUid uid, DocumentPrinterComponent comp, DocumentPrinterPrintMessage args)
    {
        var ent = new Entity<DocumentPrinterComponent>(uid, comp);
        if (ent.Comp.Printing || _timing.CurTime < ent.Comp.NextPrintTime)
            return;

        if (!_power.IsPowered(ent.Owner))
            return;

        if (!AvailableSections(ent).Any(s => s.Documents.Contains(args.Document)))
            return;

        SpawnAtPosition(args.Document, Transform(ent).Coordinates);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);

        ent.Comp.Printing = true;
        ent.Comp.NextPrintTime = _timing.CurTime + ent.Comp.PrintDelay;
        _appearance.SetData(ent, DocumentPrinterVisuals.Printing, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DocumentPrinterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Printing || _timing.CurTime < comp.NextPrintTime)
                continue;

            comp.Printing = false;
            _appearance.SetData(uid, DocumentPrinterVisuals.Printing, false);
        }
    }
}
