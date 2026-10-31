# Entities, Components, Systems (ECS)

RobustToolbox ECS as used by Maid-14. **Entity** = id (`EntityUid`, `NetEntity` over the wire) + `MetaDataComponent` + `TransformComponent`; **component** = data class attached by type; **system** = `EntitySystem` singleton holding the behaviour; **event** = the only way systems talk to each other (directed at an entity, or broadcast).

| File | Covers |
|---|---|
| `ecs/entities.md` | ids, existence and life stages, spawning, deletion order, transform hierarchy, containers, lookup |
| `ecs/components.md` | declaring components (attributes), lifecycle events, accessing/adding/removing, queries |
| `ecs/networking.md` | `Dirty`, component state get/handle, auto-generated states, prediction helpers |
| `ecs/systems-engine.md` | system shape, subscriptions, event kinds and raising, relay, time, localisation |
| `systems.md` | entry point for per-system docs of content systems (none documented yet) |
| `rsi.md` | sprites, RSI, Appearance |

Quick orientation:
- Get an id: `EntityUid` is process-local; convert with `GetNetEntity`/`GetEntity` for anything networked.
- Read data: `TryComp<T>(uid, out var c)`; hot paths cache `GetEntityQuery<T>()`; iterate with `EntityQueryEnumerator<...>`.
- Change replicated data: mutate through the owning system, then `Dirty`.
- React to things: `SubscribeLocalEvent<TComp, TEvent>`; veto with `*Attempt` events; delete with `QueueDel`.
