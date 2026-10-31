# Entities

Engine-level facts about entities. See `../ecs.md` for the overview.

## Identifiers
- `EntityUid` — a struct wrapping an `int Id`. Local to one process (server and client numbers for the same entity differ). `EntityUid.Invalid` is `0`; `Valid`/`IsValid()` means `Id > 0`. `EntityUid?` is used for "maybe no entity".
- `NetEntity` — the id that is identical on server and client. Anything that crosses the wire (events, component states, UI messages) carries `NetEntity`, never `EntityUid`. Convert with `GetNetEntity(uid)` / `GetEntity(netEntity)` (also `TryGetEntity`, and list/set/dictionary/`EntityCoordinates` variants: `GetNetCoordinates`, `GetCoordinates`, `GetEntityList`...). `NetEntity.ClientEntity` marks the range reserved for client-only entities. `IsClientSide(uid)` tells whether an entity is client-only.
- `Entity<T>` — a record struct `(EntityUid Owner, T Comp)`; also `Entity<T1,T2>` ... up to 4 components. Implicitly converts to `EntityUid` and to the component. `Entity<T?>` can be built from a bare `EntityUid` (comp resolved later). This is the preferred modern parameter shape for system methods and event handlers.
- Human readable form for logs: `ToPrettyString(uid)`; `Name(uid)` gives the display name; `Prototype(uid)` the `EntityPrototype`.

## Existence / life stage
`MetaDataComponent` exists on every entity and holds name, description, prototype, `NetEntity`, pause state, `EntityLifeStage`.
`EntityLifeStage`: `PreInit → Initializing → Initialized → MapInitialized → Terminating → Deleted`.
Checks: `Exists(uid)`, `Deleted(uid)`, `Terminating(uid)`, `TerminatingOrDeleted(uid)`. Always use `TerminatingOrDeleted` before touching an entity you got from a stored reference/event that may be stale. Null-safe overloads take `EntityUid?`.

## Creation
- `Spawn(protoId, EntityCoordinates | MapCoordinates)`, `Spawn(protoId)` (nullspace), `SpawnAtPosition`, `SpawnAttachedTo`, `SpawnNextToOrDrop(proto, target)`, `SpawnInContainerOrDrop(proto, containerOwner, containerId)`, `TrySpawnNextTo`, `TrySpawnInContainer`. `ComponentRegistry? overrides` lets the caller override component data at spawn.
- `PredictedSpawn*` variants exist for client prediction (client spawns a predicted entity that the server later reconciles).
- Proto ids are strings; typed wrappers are `EntProtoId` (an entity prototype id) and `ProtoId<T>` (any prototype kind). Use them in `[DataField]`s so YAML ids are validated.
- Creation sequence (`InitializeAndStartEntity`): components are added → `ComponentAdd` for each → `ComponentInit` for each → entity stage `Initialized` → `ComponentStartup` for each → if the world (map) is already map-initialised, `MapInitEvent` is raised → stage `MapInitialized`. Maps that have not been map-initialised (e.g. while mapping) keep entities at `Initialized`; `MapInitEvent` fires later when the map starts.

## Deletion
- `Del(uid)` deletes immediately. `QueueDel(uid)` defers to the end of the current tick (after all systems and queued events have run) — **use `QueueDel` by default** inside event handlers/updates; immediate deletion invalidates iterators and other handlers still holding the entity. `TryQueueDel` returns whether it queued.
- On the client-predicted side use `PredictedDel` / `PredictedQueueDel`.
- Order on delete: `EntityTerminatingEvent` raised on the entity and **recursively on all transform children first** (entities become `Terminating`), then the entity is detached from its parent, children are deleted recursively, then each component gets `ComponentShutdown` then `ComponentRemove`, stage `Deleted`. Deleting a parent deletes everything parented to it (contents of containers, held items, worn items, attached parts).
- Components removed with `RemComp` go through `ComponentShutdown` + `ComponentRemove` immediately; `RemCompDeferred` delays until end of tick.

## Hierarchy (TransformComponent)
Every entity has `TransformComponent` (exposed as `Transform(uid)` or `TryComp`). Position/rotation are **relative to the parent**; world values are derived. Key members: `ParentUid`, `Coordinates` (an `EntityCoordinates` = parent + local offset), `MapPosition`/`MapID`, `GridUid`, `MapUid`, `Anchored`, `LocalPosition`, `LocalRotation`, `WorldPosition`, `WorldRotation`, `ChildEnumerator`/`ChildCount`, `NoLocalRotation`.
Everything that moves/reparents goes through `SharedTransformSystem`: `SetCoordinates`, `SetParent`, `SetWorldPosition`, `SetLocalPositionRotation`, `AttachToGridOrMap`, `DropNextTo`, `PlaceNextTo`, `AnchorEntity`/`Unanchor`, `GetWorldPosition`, `GetMapCoordinates`, `InRange`, `ContainsEntity`, `GetGrid`, `GetMap`. Never write the component's fields directly.
Events: `MoveEvent` (position/rotation changed, has old/new), `EntParentChangedMessage` (reparent), `AnchorStateChangedEvent`.
Maps are entities with a map component; grids are entities with a grid component parented to a map; stations/shuttles are grids. Nullspace = no parent.

## Containers (the "inside" relation)
`ContainerManagerComponent` on the owner holds named `BaseContainer`s (`Container` = many, `ContainerSlot` = one). An entity in a container is still a transform child of the owner but has no world presence (no physics, not rendered, not found by spatial lookups). **Hands, inventory slots, storage, item slots (e.g. a gun's magazine), machine parts, bodies' organs, status-effects all are containers.**
`SharedContainerSystem`: `EnsureContainer<T>(owner, id)`, `Insert`, `Remove`, `CanInsert`, `TryGetContainer`, `TryGetContainingContainer`, `IsEntityInContainer`, `IsEntityOrParentInContainer`, `GetAllContainers`, `EmptyContainer`, `CleanContainer`, `TryFindComponentOnEntityContainerOrParent`.
Events: *attempt* (cancellable) `ContainerIsInsertingAttemptEvent` (on container owner), `ContainerGettingInsertedAttemptEvent` (on the item), `ContainerIsRemovingAttemptEvent`, `ContainerGettingRemovedAttemptEvent`; *done* `EntInsertedIntoContainerMessage`/`EntGotInsertedIntoContainerMessage` (owner / item side), `EntRemovedFromContainerMessage`/`EntGotRemovedFromContainerMessage`. "Inserted" vs "GotInserted" = same occurrence, raised on owner vs on the item.

## Lookup / queries on the world
`EntityLookupSystem` answers "which entities are near / intersect this area / are in this range" (with `LookupFlags`); physics has its own queries. For "all entities with component X" use component queries below, not the lookup.
