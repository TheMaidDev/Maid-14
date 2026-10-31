# Controversial / needs-decision items (RT 266 -> 293.0.1)

Legend: **OPEN** = needs your decision or playtest, **NOTE** = done, but please be aware.
Everything here was found while updating; "mechanical" renames are not listed.

## Build / tooling
- **NOTE C# 14 everywhere** (`MSBuild/Content.props`, copied from Goob). Was `LangVersion 12`. RT generated code needs it.
  Possible subtle overload-resolution changes (e.g. new first-class span conversions) in hand-written code.
- **NOTE Package bumps (as Goob):** EF Core Design/Sqlite.Core 9.0.1 -> 10.0.0, Npgsql.EFCore 9.0.3 -> 10.0.0, OpenTK 4.9.4, CsvHelper 33.1.0.
  **OPEN:** EF/Npgsql 10 touch the production DB layer. No migration/model files were edited. Needs a Postgres + SQLite smoke test.
- **NOTE NuGet audit** warnings (NU1902/NU1903) on transitive `System.Security.Cryptography.*` 9.0.0 in `Content.Server.Database`.
- **NOTE `RA0049`/`RA0051`** (`[Dependency]` fields should be `partial` / not `readonly`) are suppressed in `Content.props`;
  RT says they become errors in the future. Hundreds of sites.
- **NOTE** Explicit package refs added (RT no longer exposes its packages transitively): YamlDotNet, Serilog, SixLabors.ImageSharp,
  Pidgin, Robust.Shared.AuthLib, prometheus-net, ObjectPool, Moq.
- **OPEN CI:** `.github/workflows` edits (.NET 10) are in `RT_UPDATE/workflows-dotnet10.patch`; the bot token has no `workflow` scope, apply with `git apply`.
- **NOTE** `Pow3r` shows OpenTK NU1608 warnings (tool project).
- **NOTE** `.sln` -> `.slnx`; run `dotnet run --project RobustToolbox/Tools/Robust.SolutionGen -- update` after every RT feature update.

## Silent behaviour changes in RT (playtest!)
- **OPEN XAML `StyleClasses="..."` now replaces all classes** (RT 285). Stock `Button` is patched for compat, custom controls that add
  classes in code and then get `StyleClasses=` in XAML lose them. Needs a UI visual pass.
- **OPEN Physics events:** StartCollide/EndCollide are buffered until the end of the substep (268). Gameplay relying on same-tick collide events needs a playtest.
- **OPEN `SpawnAtPosition(EntityCoordinates)`** uses the anchor entity's rotation (279). Entities spawned that way may rotate differently.
- **OPEN `ReallyBeIdle` tick count 25 -> 5** (279).
- **OPEN Box2/Box2i/UIBox2i validate their inputs** (279/286/280). Inverted/negative boxes now assert/throw at runtime: see "YAML data fixes".
- **OPEN Partial prototypes:** `!CombineIndex` required to modify existing list elements (289). I found no usage, not exhaustively checked.
- **OPEN Replay** changed to lazy loading (288); old replays are not guaranteed to load.
- **OPEN Transform lerping** was reworked in 292 and reverted in 293; no content code touched the removed render API.
- **OPEN `ReAnchorEvent` removed** (292). Subscriptions in Airtight, NodeContainer, ExtensionCable, RadiationBlocker, Docking and ContainmentFieldGenerator
  were commented out (marked `MAID rt-update`); the existing `AnchorStateChangedEvent` handlers are now raised for grid splits too. Test grid splitting (explosions/shuttle damage) for pipes, cables, airlocks.
- **OPEN `IMapManager.Restart()` removed:** the call in `GameTicker.RoundFlow` was dropped (`FlushEntities()` just above already deletes all maps; Goob does the same).
- **NOTE Component interning / PVS async serialization / ReflectionManager rewrite:** perf/memory only, test under load.

## YAML / data fixes required by the new engine
- **Inverted Box2 fixture bounds** (assert in `Box2`) fixed by normalizing min/max in: railing, strobe/base/ground lighting, torch, flock light, RMC platform. Same as Goob. The collision shape of these entities changes from an inverted (degenerate) box to the intended one.
- **Inverted Box2i item shapes** fixed in resomi and projectiles (`shape:` lists).
- **`PaperVisuals` margins:** component fields are now `Thickness` (RT 271), 42 YAML lines reordered from Box2 order (l,b,r,t) to Thickness order (l,t,r,b); visual result should be identical.
- **`Timer` component removed** (RT 275): 63 `- type: Timer` entries removed from prototypes and maps (maps: Chloris, gate, ScoupidiaMaid, etc.).
- **`MultipleTool.behavior` and `Tool.qualities`** scalars -> lists (flags serializer removed).
- **Action icons `x.rsi/state.png`** -> `{ sprite, state }` (RSI texture paths no longer allowed).
- **`Occluder.boundingBox`** -> `polygon` (RT 287; two wall prototypes).
- **`BodyPartAppearance` `iD:`** -> `id:` (field naming of acronyms changed).
- **Locale:** removed duplicate `cmd-replay-toggleui-desc` (now in engine); `getreput/modifyreput/setrep` help strings no longer use unsupplied variables.

## Code decisions made by me that may deserve a second look
- **NOTE `string` prototype-id fields became `ProtoId<T>`/`EntProtoId`** (also dictionary keys, sets, lists). Matches Goob. Callers use `.Id`/implicit conversions.
- **NOTE `PrototypeFlags<ToolQualityPrototype>` -> `HashSet<ProtoId<ToolQualityPrototype>>`** (flags serializer removed; would have failed only at runtime).
- **NOTE `[ValidatePrototypeId]` consts -> `static readonly ProtoId<T>`**; no longer `const`.
- **NOTE `IWeightedRandomPrototype<T>`** generic interface + `Pick<T>` copied from Goob; `WeightedRandom{Ore,Species,Entity}Prototype` use `ProtoId<T>` keys.
- **NOTE Non-system classes** (console commands, overlays, UI controls, game states) cannot `[Dependency]`-inject entity systems; they now get `SharedMapSystem` via `IEntityManager.System<>()` (found via the YAML linter, not the compiler).
- **NOTE (from your test report)** `MappingState` (a game `State`) also cannot inject `SharedMapSystem` -> `IEntityManager.System<>()`; `Task.WhenAll(a, b)` in `ParallaxManager` binds to the C# 13/14 `params ReadOnlySpan` overload and breaks the RT sandbox (ILVerify `InlineArrayAsReadOnlySpan`), now an explicit array like Goob; `EnchantVisualsSystem` set a legacy `PostShader` before the sprite was initialized (RT 286 warns, tests fail), now uses `SetPostShader` with an id on Startup/state. Re-check any new `params`/collection-expression calls in sandboxed assemblies (`Content.Client`, Shared, Goob*).
- **NOTE (from a client crash report)** `CustomRichTextEntry.DrawBoxContent` built an inverted `UIBox2` (top > bottom) -> `DebugTools.Assert` in Debug/DebugOpt builds (asserts are compiled out in Release, but the box was still degenerate). Now normalized (min/max); `FancyBorderRenderer` clamps its derived box. Other UI code that computes `Box2`/`UIBox2` from arithmetic may hit the same assert; they only show up when that UI is drawn in a Debug build, so click through chat/UI in a Debug client.
- **NOTE (from a client crash report)** engine `IEyeManager.GetWorldViewport()` builds an inverted `Box2` when the eye is rotated (assert in Debug). `Goobstation.Client` `ParticleSystem` now uses its own min/max helper (same as Goob upstream). `ExamineSystem` still calls `GetWorldViewbounds()` (also unchanged in Goob) and can hit the same assert in a Debug client with a rotated eye; Release builds are not affected.
- **NOTE `BoundUserInterfaceMessageAttempt` is by-ref** (292); `SharedZombieBlobSystem` handler updated (runtime-only error).
- **NOTE `EntityUid.SpawnTimer/SpawnRepeatingTimer`** (removed with `TimerComponent`) replaced by `Content.Shared._Maid.Utils.EntityTimerExtensions` (callback skipped if the entity is gone). Used by Flammable, Matchstick, PoweredLight, Spawner. Upstream rewrote these into update loops; consider doing the same.
- **NOTE `SelectableAmmoSystem`:** hitscan provider name lookup keeps old behaviour (hitscan id looked up as an *entity* prototype). Probably a pre-existing bug.
- **NOTE `OccluderComponent.BoundingBox` -> `LocalBounds`** in the examine occlusion check (occluders can be convex polygons now).
- **NOTE Primary-constructor DoAfterEvents** (`CarveRune`, `SmHalberdExecute`, `TeslaBlast`) rewritten with normal ctors (serialization generator).
- **NOTE Events with primary ctors:** `[DataField]` removed (as Goob). `{ get; }` / `private init` on prototype/data properties -> `private set`.
- **NOTE `BlobTypedStorage<T>` / `BlobChemTypedStorage<T>`:** DataFields moved to the concrete subclasses (generator cannot handle generic DataDefinition bases).
- **NOTE `FindGridsIntersecting(MapId, Box2Rotated)`** enumerable replaced by callbacks (Salvage magnet, AmbientOcclusion overlay).
- **NOTE `ConstructionSystem` copies `Anchored`** via `TransformSystem.TryAnchor`.
- **NOTE Test fixes:** IntegrationTests/MapRenderer `IMapManager` -> `SharedMapSystem`; `PuddleTest` materializes tiles before editing them (new debug assert); Toolshed tests no longer reference the now-internal RT `LocTest`.

## Verification status
- Builds: all projects in `SpaceStation14.slnx` that I built compile (Shared, Server, Client, Goobstation.*, Replay, Packaging, Tools, Tests, IntegrationTests, MapRenderer, YAMLLinter, Benchmarks, Pow3r, PatreonParser) with the repo's warnings-as-errors.
- YAML linter: **No errors found**.
- `Content.Tests`: 368 passed, 0 failed, 1 skipped.
- Integration tests run (one at a time, low RAM): EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps, PrototypeSaveTest, PrototypeTests, GridJoinTest, Airlock/Doors, PowerTest (15), Construction (ActionValid + Interaction 24), PostMapInit GameMapsLoadable (8) + ShuttlesLoadable, Shuttle, DeviceLinking, InteractionSystem (5), Body, Storage, Tools, Fluids (23, after fixing one test bug). **The full suite has not been run** (RAM); run it on CI.
- Re-run after your report: MappingEditorTest, SandboxTest, WeldableTests, XenoArtifactTest, MappingTest, EntityTest.SpawnAndDeleteEntityCountTest all pass (Debug config).
- **Not done:** real client+server boot with a connected player, DB smoke test, UI visual pass.
