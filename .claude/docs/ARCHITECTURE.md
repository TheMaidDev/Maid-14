# Maid-14 architecture

Maid-14 = Space Station 14 (SS14) on the **RobustToolbox** engine (C#/.NET 9, YAML data, Fluent localisation), forked through Goob Station. Docs in this folder:
- `ecs.md` — ECS overview and index of the engine-mechanics files in `ecs/` (`entities.md`, `components.md`, `networking.md`, `systems-engine.md`).
- `systems.md` — entry point for content-system docs (none documented yet). Per-system docs live in `systems/<system>.md` (one file per system, added over time; subsystems may get their own files).
- `rsi.md` — sprite packaging, sprite layers, the Appearance bridge, equipment art naming.

**Where to read first**
- Any code change: this file, then `ecs.md`.
- Data and replication: `ecs/components.md`, `ecs/networking.md`.
- Behaviour and events: `ecs/systems-engine.md`, then the matching `systems/<system>.md` if it exists.
- Visuals, sprites, art: `rsi.md`.
- Spawning, containers, transforms: `ecs/entities.md`.

## 1. Repository map

| Path | What it is |
|---|---|
| `RobustToolbox/` | Engine submodule (currently **v266.0.0**; upstream Goob is on v293). Projects: `Robust.Shared` (ECS, physics, maps, prototypes, serialization, networking), `Robust.Server`, `Robust.Client` (rendering, UI toolkit, audio, input, resources). |
| `Content.Shared/` | Game code that runs on **both** server and client: component definitions, rules that must agree for prediction, events, prototype classes, enums for visuals/UI keys. |
| `Content.Server/` | Authoritative-only: spawning, persistence, power/atmos simulation, game rules, admin, chat routing, anything hidden from clients. |
| `Content.Client/` | Presentation: visualizers, UI windows (XAML), overlays, input, audio cues. |
| `Content.Shared.Database/`, `Content.Server.Database/` | EF Core models/migrations. |
| `Content.Goobstation.{Shared,Server,Client,Common,Maths,UIKit}` | Goob's own layer on top of upstream SS14 content. |
| `Content.Tests`, `Content.IntegrationTests`, `Content.Benchmarks`, `Content.YAMLLinter`, `Content.MapRenderer`, `Content.Packaging`, `Content.Replay`, `Content.Tools`, `Content.ModuleManager`, `Content.PatreonParser` | Tooling/CI. |
| `Resources/Prototypes/` | YAML data: entities (`Entities/`), `Access`, `Damage`, `Body`, `Chemistry`, `Catalog`, `Datasets`, `InventoryTemplates`, game presets, etc. Many fork prefixes coexist: `_Maid`, `_Goobstation`, `_Corvax*`, `DeltaV/_DV`, `_Impstation`, `_EinsteinEngines`, `_Floofstation`, … |
| `Resources/Textures/` | RSI folders (see `rsi.md`); `_Maid/…` is Maid art. |
| `Resources/Audio`, `Maps`, `Fonts`, `ServerInfo`, `ConfigPresets`, `Changelog` | Assets/config. |
| `Resources/Locale/<lang>/` | Fluent `.ftl` text. `en-US`, `ru-RU` (the main one for Maid), `nl-NL`. |
| `Tools/` | Scripts (changelog, REUSE headers, mapping tools). `Tools/_Maid` for Maid's. |
| `.github/workflows/` | CI. All GitHub PRs are mirrored locally as `origin/pr/<n>`. |

**Maid code** lives in `_Maid/` folders (in each Content project, `Resources/Prototypes/_Maid`, `Textures/_Maid`, locale). Other forks' markers seen in upstream files: `// Goobstation`, `// WD EDIT`, `// Shitmed`, `<Trauma>…</Trauma>`, `// DeltaV`, `// MAID …` for Maid's own edits.

## 2. The three code sides and how they connect

- `Content.Shared` is compiled into both `Content.Server` and `Content.Client`. The server is the authority; the client does prediction and presentation.
- Server → client: replicated component state, networked events, UI messages (engine layer: `Robust.Server` / `Robust.Client` on top of `Robust.Shared`).
- Client → server: input commands, predictive events, UI messages.
- **Shared logic runs on both sides**: the client executes it *predictively* for the local player and gets corrected by authoritative server state. This is why gameplay rules (interaction, hands, inventory, damage, doors, locks, storage) live in `Content.Shared`.
- Per-feature split: `Shared…System` (rules), server part (side effects that need authority; guarded with `_net.IsServer` when a shared path needs it), client part (visuals/UI, guarded with `_timing.IsFirstTimePredicted` for one-shot effects).
- **Data flow**: input → client `InputSystem` → command binding (e.g. interaction `Use`) → shared system runs (predicted) *and* the input is sent to the server → server runs the same shared code → components are `Dirty`'d → game state sent to clients in PVS → client applies state (`ComponentHandleState`) → visuals update via Appearance/visualizers. Details in `ecs/networking.md`.
- **Client-only components** (e.g. `SpriteComponent`, visualizers) appear in shared prototypes; the server skips them via the explicit `IgnoredComponents.List` in `Content.Server/Entry/` and by ignoring any missing component whose name ends in `Visuals`. **Server-only components** (e.g. power net nodes) are skipped on the client because the client calls `IgnoreMissingComponents()` (any component type it doesn't know is silently ignored). A *new* client-only component therefore needs an entry in the server ignore list or the server errors on the prototype.
- **Entry points**: each Content project has `Entry/EntryPoint.cs` registering IoC services and starting module init.

## 3. Data-driven layer (what lives in YAML)

- **Entity prototypes**: `- type: entity`, `id`, `parent` (single or list; inheritance merges components), `abstract: true`, `name`/`description`/`suffix`/`categories`, `components:` list of `- type: <ComponentName minus "Component">` with field overrides (field names = camelCase `[DataField]` names). `!type:SomeClass` selects polymorphic types (e.g. conditions, effects, shapes). Enum values are written `enum.EnumType.Member`. Child prototypes may replace/remove inherited components.
- **Other prototype kinds** (each a C# class with `[Prototype]` + `IPrototype`): damage types/groups/modifier sets, item sizes, inventory templates, tool qualities, tags, alerts, reagents/reactions, jobs, game rules, loadouts, datasets, etc. Reference by id with `ProtoId<T>` / `EntProtoId`.
- **Hot reload**: prototypes reload in dev; component field changes in YAML take effect on new entities.
- **Localisation**: `Loc.GetString("key", ("arg", value))`, entity names/descriptions via `ent-<ProtoId>` keys (`.name`, `.desc`, `.suffix`) in `Resources/Locale/<lang>/**/*.ftl`. UI XAML uses `Loc` too. Maid's primary language is Russian (`ru-RU`); English strings must still exist.
- **CVars**: config vars declared in `Content.Shared/CCVar/CCVars.*.cs` (Maid ones under `_Maid/CVars`), set from `Resources/ConfigPresets` or server config.
- **Maps**: authored in the map editor/YAML; reference prototypes by id and carry per-entity overrides.

## 4. Runtime world model

- **Maps** (`MapComponent`) are roots; **grids** (`MapGridComponent`) are tile-structure entities parented to maps (stations, shuttles, asteroids); everything else is parented to a grid, a map, or another entity (container contents). Position is always relative to the parent.
- **Containers** express "inside" (inventory, hands, storage, item slots, machine parts, body parts, status effects).
- **Physics** (Box2D-derived) via `PhysicsComponent`/`FixturesComponent`; collision/trigger events are directed events on entities.
- **PVS**: the server only sends entities within a client's view/range, so client code can see only part of the world — never assume a distant entity exists on the client.
- **Time**: simulation ticks at a fixed rate (`net.tickrate`, default **30**); `IGameTiming.CurTime` is the clock for cooldowns; paused entities (on paused maps) are skipped by update queries.

## 5. Systems layering in Content (how the big pieces relate)

Relations between the big pieces, as text:
- Clicks and UI input go to **Interaction**, which dispatches to Verbs, Hands, item use, DoAfter, Tools, Storage, ItemSlots, Doors, Locks and similar.
- **ActionBlocker** sits beside Interaction: attempt events cancelled by MobState, stun, restraints and others decide whether an action is allowed.
- **Damage** feeds **MobState**, which in turn affects Standing, Movement, Alerts and overlays.
- **Inventory** relays events from the wearer to equipped items (armor, speed, vision, slip and so on).
- **Power** (server simulation) produces the `Powered` flag, which machines, lights and UIs consume.
- **Appearance** carries state to client visualizers, which set sprite layers (see `rsi.md`).
- The same few primitives recur: **attempt event → cancel**, **modifier event → aggregate**, **done event → react**, **relay event → forward to equipment/parts**, **component as capability**, **container as location**, **appearance key as visual state**.
- The fork adds large vertical subsystems on top of upstream: body/wound model ("Shitmed", complex bodies where damage is applied per body part), antagonists/game modes from Goob, and Maid's economy, mood, adaptive/dynamic game modes, TTS, ghost/ERT recruitment, trade shuttle console. Find them under the relevant `_Maid`/`Goobstation` folders.
