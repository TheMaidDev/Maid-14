# Systems (engine mechanics)

How `EntitySystem`s are written, ordered, and communicate through events. Concrete content systems are indexed in `../systems.md`.

## Shape
```
public sealed class FooSystem : EntitySystem   // or Shared base + Server/Client subclass
{
    [Dependency] private readonly BarSystem _bar = default!;   // injected before Initialize
    public override void Initialize() { SubscribeLocalEvent<FooComponent, SomeEvent>(OnSome); }
    private void OnSome(Entity<FooComponent> ent, ref SomeEvent args) { ... }
    public override void Update(float frameTime) { ... }   // every tick (server) / predicted tick (client)
}
```
- Exactly one instance of each system per side. Discovered by reflection; no registration needed. `[Dependency]` also injects managers (`IGameTiming`, `IPrototypeManager`, `INetManager`, `IRobustRandom`, `ILocalizationManager`...).
- `Update(frameTime)` runs per simulation tick, and on the client also during prediction. It is **skipped on the client when prediction is disabled** unless the system sets `UpdatesOutsidePrediction = true` (client-only/visual systems that must always tick set it). `FrameUpdate` runs per rendered frame (client-visual only). Ordering between systems: `UpdatesAfter`/`UpdatesBefore` lists, or `before:`/`after:` arguments on `SubscribeLocalEvent` for handler order.
- Convention for sides: `SharedFooSystem` (abstract or full) in `Content.Shared`; `FooSystem` in `Content.Server` and/or `Content.Client` inherit/extend it. A system with no `Shared` prefix in `Content.Shared` runs on both sides unchanged. Shared system code must never reference server-only or client-only types.
- Systems own their component data: other code calls the system's public API. `[Access(typeof(FooSystem))]` on the component enforces it.

## Events
- Subscription forms:
  - `SubscribeLocalEvent<TComp, TEvent>(handler)` — **directed**: runs only when the event is raised on an entity that has `TComp`. Handler `(Entity<TComp> ent, ref TEvent args)` for `[ByRefEvent]` events, or `(EntityUid uid, TComp comp, TEvent args)` for class events. Several systems/components may subscribe to the same event on the same entity; order via `before:/after:` types.
  - `SubscribeLocalEvent<TEvent>(handler)` — **broadcast**: runs for every raise.
  - `SubscribeNetworkEvent<T>((ev, EntitySessionEventArgs args) => ...)` — receive a networked event (`args.SenderSession` = who sent it on the server). `SubscribeAllEvent<T>` = local + network.
- Raising: `RaiseLocalEvent(uid, ev)` (directed only), `RaiseLocalEvent(uid, ev, broadcast: true)` (directed **and** broadcast subscribers), `RaiseLocalEvent(uid, ref ev)` for by-ref, `RaiseLocalEvent(ev)` broadcast only, `QueueLocalEvent` (deferred to end of the system updates), `RaiseNetworkEvent(ev)` / `(ev, session|filter|uid)`.
- Event kinds: classes deriving `EntityEventArgs`; `HandledEntityEventArgs` (`Handled` flag — first handler to set it stops "who acts"); `CancellableEntityEventArgs` (`Cancel()`; the *attempt* pattern); or `[ByRefEvent] record struct` (preferred for new code; mutate fields through `ref`, return data by writing to the struct). Networked events must be `[Serializable, NetSerializable]`.
- **Attempt/query pattern** (used everywhere): raise `XAttemptEvent`; any interested component cancels or modifies; caller checks the result. **Modifier events** (`DamageModifyEvent`, `RefreshMovementSpeedModifiersEvent`...) are raised, handlers contribute, the raiser reads the aggregate. **Done events** announce the result after the fact.
- **Relay**: an entity forwards an event to related entities (worn items, held items, a body's parts). E.g. inventory relays chosen events to every equipped item matching `IInventoryRelayEvent.TargetSlots`, wrapped as `InventoryRelayedEvent<T>`; hands do the same with `HeldRelayedEvent<T>`. `SubscribeWithRelay` registers a handler for the direct event plus the inventory/held relay versions.

## Time in systems
`_timing.CurTime` (a `TimeSpan`, pause-aware, predicted) is the clock for cooldowns/timers; store "next allowed time" as `TimeSpan` fields (with `[AutoPausedField]`) instead of counting ticks. `IGameTiming.CurTick` is the network tick.

## Localization & prototypes from systems
`Loc.GetString("fluent-key", ("var", value))` for text (Fluent `.ftl` files in `Resources/Locale/<lang>/`). `IPrototypeManager`: `Index<T>(id)`, `TryIndex`, `EnumeratePrototypes<T>()`.
