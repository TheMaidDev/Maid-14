# Components

Engine-level facts about components. Replication of component data is in `networking.md`. See `../ecs.md` for the overview.

## Declaration (the attribute vocabulary)
```
[RegisterComponent]            // makes the type a real component (required)
[NetworkedComponent]           // state is replicated server -> client
[AutoGenerateComponentState]   // generate GetState/HandleState from [AutoNetworkedField]s
[AutoGenerateComponentPause]   // generate unpause handling for [AutoPausedField] TimeSpans
[Access(typeof(SomeSystem))]   // only listed types may *write* it (analyzer-enforced)
public sealed partial class FooComponent : Component
```
- Class name **must end in `Component`**; the YAML name is the class name minus that suffix (`type: Foo`). `[ComponentProtoName("X")]` overrides.
- Classes must be `partial` when source-generators are used (they nearly always are) and are normally `sealed`.
- Components are **classes**, one instance per type per entity (`AddComp<T>(uid)` on an entity that already has it is an error unless `overwrite: true`; `EnsureComp` is idempotent).
- Fields:
  - `[DataField]` — loaded from YAML/prototype (YAML key = member name in camelCase unless a name is given; `required: true`, `readOnly: true`, `customTypeSerializer:` options exist). Fields **without** `[DataField]` are runtime-only.
  - `[AutoNetworkedField]` — replicated by the generated state (needs `[AutoGenerateComponentState]`). `EntityUid`, `EntityUid?`, `EntityCoordinates`, and `List/HashSet/Dictionary` of them are converted to `NetEntity` automatically. `[AutoGenerateComponentState(fieldDeltas: true)]` sends only changed fields (then dirty with `DirtyField`).
  - `[AutoPausedField]` — a `TimeSpan` (or nullable) that is shifted forward when the entity is unpaused, so "next fire time" fields don't expire while paused.
  - `[ViewVariables(VVAccess.ReadWrite)]` — shows/edits in the in-game VV tool.
- `[Access(...)]` defaults: owner type and friends = read/write, everyone else = read-only. Violations are compile-time analyzer errors; to change a protected component from elsewhere, call the owning system's API.
- Any file with `sealed partial class FooComponent` can add `: Component` plus extra interfaces; `ComponentState` classes (hand-written networking) are `[Serializable, NetSerializable]`.

## Lifecycle events (raised on the entity, subscribed per component)
Per component, in order: `ComponentAdd` → `ComponentInit` → `ComponentStartup` → (`MapInitEvent` if the entity/map is map-initialised) → … → `ComponentShutdown` → `ComponentRemove`.
- `ComponentInit`: component and its siblings exist; set up own state, don't assume other systems' reactions.
- `ComponentStartup`: all components of the entity are initialised; safe to read siblings. Most "setup" logic goes here.
- `MapInitEvent`: once-per-world-start work (rolling random values, starting timers, spawning contents). Not re-run when a saved map reloads.
- `ComponentShutdown`: clean up anything set elsewhere (containers, other entities, UI, effects). Also fires when the entity is deleted.
- Adding a component to an entity that is already running immediately runs add→init→startup(→mapinit) for that component; removing runs shutdown→remove.
- `ComponentLifeStage` values (`Adding … Running … Stopped … Deleted`) are available as `comp.LifeStage`, `comp.Running`, `comp.Deleted`, `comp.Initialized`.
- Entity pause: `EntityPausedEvent` / `EntityUnpausedEvent(PausedTime)`.

## Accessing components (all on `EntitySystem`, mirror on `EntityManager`)
| Need | Call |
|---|---|
| get or fail | `Comp<T>(uid)` (throws if absent), `CompOrNull<T>(uid)` |
| get safely | `TryComp(uid, out T? comp)` (also accepts `EntityUid?`) |
| presence | `HasComp<T>(uid)` |
| add / ensure / remove | `AddComp<T>(uid)`, `EnsureComp<T>(uid)` / `EnsureComp(uid, out comp)`, `RemComp<T>(uid)`, `RemCompDeferred<T>(uid)` |
| enumerate | `AllComps(uid)` |
| copy | `CopyComp`, `CopyComps`, `TryCopyComponent` |
| resolve optional param | `Resolve(uid, ref comp)` — fills `comp` if null; logs an error if missing unless `logMissing: false`. Public system APIs take `T? comp = null` + `Resolve` so callers may pass what they already have. |

`Dirty(uid, comp)` / `Dirty(ent)` — mark changed for replication. `DirtyField(uid, comp, nameof(Comp.Field))`/`DirtyFields` for delta components. `DirtyEntity(uid)` for entity-level data (transform/meta are handled by their systems).

## Iterating many entities
- `EntityQueryEnumerator<A,B,...>()` — `while (q.MoveNext(out var uid, out var a, out var b))`. **Skips paused entities.** Used in `Update`.
- `AllEntityQuery<A,B>()` — same shape but includes paused entities.
- `GetEntityQuery<T>()` → cached `EntityQuery<T>` struct (`TryComp`, `HasComp`, `Comp`, `Resolve`, `Get`). Fetch once in `Initialize` into a `_fooQuery` field for hot paths.
- `EntityQuery<T>()` (IEnumerable) exists but is slower/older.
