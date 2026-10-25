# RSI (Robust State Image) and sprites

Facts about how art is packaged and how it reaches the screen. Engine side (`Robust.Shared/Resources/RsiLoading`, `Robust.Client` `RSIResource`, `SpriteComponent`/`SpriteSystem`, `AppearanceSystem`) is stable; content side (clothing/item/visualizer systems) changes more.

## 1. What an RSI is on disk
A **directory** named `something.rsi` under `Resources/Textures/` containing:
- `meta.json` — the manifest.
- one `<state name>.png` per state (file name = state name exactly).

(`.rsic` is a single-PNG variant with the JSON embedded in a PNG metadata field; the game uses the folder form.)

### meta.json fields
- `version`: int (1).
- `license`: string such as `"CC-BY-SA-3.0"` (project policy: mandatory).
- `copyright`: free text with the origin, author and commit/link of the art (project policy: mandatory).
- `size`: `{ "x": 32, "y": 32 }`, the frame size in pixels, shared by every state.
- `states`: list of `{ name, directions?, delays?, flags? }`.
- `load`: optional `{ "srgb": bool }`, texture load parameters.
- `directions` ∈ **1, 4, 8**; omitted = 1. Anything else fails loading.
- `delays` is a **list per direction of lists of frame durations in seconds**: `[[0.1,0.1,0.1]]` for a 1-direction 3-frame animation, `[[1],[1],[1],[1]]` for a 4-direction still. The outer length must equal `directions`. Omitted = one frame per direction, no animation. Durations are folded across directions internally.
- `flags` / selectors are parsed but ignored by the engine today.
- State names must be unique within an RSI (duplicate = load error).

### PNG layout of a state
The image must be an exact multiple of `size` in both axes. Frames are read **row-major, wrapping at the image width**, in the order: *all frames of direction 0, then all frames of direction 1, …*. So a 4-direction 1-frame state is 4 cells (typically a 4×1 or 2×2 grid), a 4-direction 3-frame state is 12 cells.
Direction order (`RsiDirection`): `South(0), North(1), East(2), West(3)`, and for 8-dir: `SouthEast(4), SouthWest(5), NorthEast(6), NorthWest(7)`.
At load the game packs all states into one atlas texture, so many tiny RSIs are cheap; mismatched metadata/PNG sizes are the usual load failure (`RSILoadException`).

### Where assets are referenced from
Paths in YAML/code are relative to `Textures/` (`sprite: Objects/Tools/crowbar.rsi`). Maid-owned art lives under `Textures/_Maid/...`; upstream forks use their own prefixes (`_Goobstation`, `_Corvax`, `_DV`, …). The YAML sprite `icon`/`SpriteSpecifier` form is `{ sprite: <rsi path>, state: <state> }` (or a bare `texture:` PNG).

## 2. Sprites on entities
`SpriteComponent` (client-only data; it appears in entity prototypes but is never networked) holds a list of **layers**. YAML form:
```yaml
- type: Sprite
  sprite: Objects/Tools/wirecutters.rsi      # default RSI for all layers
  drawdepth: Items                            # DrawDepth tag
  layers:
  - state: cutters
    map: [ "enum.DamageStateVisualLayers.Base" ]   # layer key(s)
  - state: cutters-cutty-thingy
    shader: unshaded                          # also: visible, color, offset, scale, rotation, sprite (per-layer RSI), renderingStrategy
```
- Shorthand: `sprite:` + `state:` at the component level makes a single layer.
- Layer fields (`PrototypeLayerData`): `sprite`, `state`, `texture`, `shader`, `scale`, `rotation`, `offset`, `visible`, `color`, `map` (set of keys), `renderingStrategy`, `cycle`, shader parameter copying.
- Component-level fields: `visible`, `drawdepth`, `scale`, `rotation`, `offset`, `color`, `snapCardinals`, `noRot`, `overrideDir`/`enableOverrideDir`, `overrideContainerOcclusion`.
- **Layer keys** (`map:`) are what code addresses. Keys are usually enum values (written `enum.SomeVisualLayers.Base` in YAML, a C# `enum` in code) or strings. Without a key a layer can only be addressed by index.
- Direction: a sprite picks the frame from the entity's world rotation (`snapCardinals` snaps to 4; `noRot` ignores rotation; `overrideDir` forces one).
- A sprite whose entity sits in a container is hidden (`ContainerOccluded`) unless `overrideContainerOcclusion`.

### SpriteSystem API (client)
Operate through `SpriteSystem` with `Entity<SpriteComponent?>`: `LayerSetRsiState(ent, key|index, state)`, `LayerSetVisible`, `LayerSetColor`, `LayerSetOffset`, `LayerSetScale`, `LayerSetRotation`, `LayerSetRsi`, `LayerSetTexture`, `LayerSetSprite`, `LayerSetAutoAnimated`, `LayerMapSet/Get/TryGet/Remove/Reserve`, `AddLayer/AddBlankLayer/AddRsiLayer/AddTextureLayer/RemoveLayer`, `LayerExists`, `SetVisible`, `SetColor`, `SetDrawDepth`, `SetOffset`, `SetRotation`, `SetScale`, `SetBaseRsi`, `GetState`/`GetIcon`/`GetPrototypeIcon` (for UI icons), `CopySprite`. `sprite.BaseRSI` is the default RSI; `RSI.TryGetState(name, out state)` tests whether a state exists.
Sprites exist only on clients; shared/server code cannot touch them. Anything that must be visible is exposed through Appearance (below) or entity-specific client systems.

## 3. Appearance: how game state becomes sprite state
`AppearanceComponent` (networked) is a dictionary `Enum key → object value` ("appearance data").
- Set from shared/server logic: `_appearance.SetData(uid, SomeVisuals.Key, value)` (also `RemoveData`, `CopyData`, `AppendData`, `TryGetData<T>`). `SetData` is a no-op when the value is unchanged, skipped while the client is applying a server state, dirties the component (so it replicates) and queues a visual update. Keys are enums (`[Serializable, NetSerializable]` so they can be sent); values must be serialisable value types / enums / strings.
- The client `AppearanceSystem` processes the queue each frame (`FrameUpdate`) and raises **`AppearanceChangeEvent`** (by-ref; fields `Component`, `AppearanceData`, `Sprite`) on the entity. It is also queued when the appearance component starts up and when a server state changes the data (including after re-entering PVS).
- Consumers:
  1. **`VisualizerSystem<TComp>`** (client): subclass, override `OnAppearanceChange(uid, comp, ref AppearanceChangeEvent)`, read data via `AppearanceSystem.TryGetData<T>(uid, key, out var v, args.Component)`, and call `SpriteSystem.Layer*`. Used when logic is non-trivial (e.g. a lock: pick the "locked"/"unlocked" state, hide while open).
  2. **`GenericVisualizerComponent`** (YAML only, no code): `visuals: { <appearance key>: { <layer key>: { <value>: <layer data> } } }` — e.g. `enum.StorageVisuals.Open: { enum.StorageVisualLayers.Door: { True: { visible: true }, False: { visible: false } } }`, or `Paper: { state: paper }`. The data value is converted with `ToString()` and matched against the value keys (so enum members are written by name); a layer key that doesn't exist yet is created (reserved blank layer) on first use.
  3. Client systems that listen to domain events directly (damage overlays, mob state, humanoid layers).
- Convention: each feature defines its own `…Visuals` enum (keys) and `…VisualLayers` enum (layer keys) in `Content.Shared` next to its component, so shared logic sets the data and client + YAML refer to the same enum.

## 4. Equipment and item art naming (content convention, client-side systems)
These are **state-name conventions in an item's RSI**; the client builds worn/held layers by looking them up.
- `icon` — by convention the state shown as the item's icon (ground sprite, and what UI icon generation `GetPrototypeIcon` picks up from the prototype's sprite).
- **Held**: `inhand-left` and `inhand-right` (4 directions each). `ItemComponent.HeldPrefix` prepends `"{prefix}-"` (e.g. `on-inhand-left`) so one RSI can hold several variants; per-hand explicit layer lists can be declared in `ItemComponent.InhandVisuals`. RSI used = `ItemComponent.RsiPath` if set, else the entity's `Sprite` base RSI.
- **Worn**: `equipped-<SLOT>` (4 directions). `<SLOT>` comes from the slot name through a fixed map in the client clothing system: `head→HELMET, eyes→EYES, ears→EARS, mask→MASK, outerClothing→OUTERCLOTHING, jumpsuit→INNERCLOTHING, neck→NECK, back→BACKPACK, belt→BELT, gloves→HAND, shoes→FEET, id→IDCARD, pocket1→POCKET1, pocket2→POCKET2, suitstorage→SUITSTORAGE`; Maid adds `socks→SOCKS, underweart→UNDERWEART, underwearb→UNDERWEARB`. `ClothingComponent.EquippedPrefix` → `"{prefix}-equipped-<SLOT>"`; `EquippedState` overrides the whole name; `RsiPath` overrides the RSI.
- Wearer variants (checked in this order, falling back to the plain name): body-type suffix (`…-<bodytype>`, a WD feature) then species suffix (`…-<species id>`, e.g. `equipped-INNERCLOTHING-Vox`). Explicit `ClothingComponent.ClothingVisuals` (per slot or `slot-species`) beats the naming convention.
- Mapping to the example RSI of a crowbar-like belt item: `icon`, `inhand-left/right`, `equipped-BELT`, plus `storage` (grid-storage preview, `ItemComponent.StoredSprite`).
- When an item's visuals change while held/worn, `ItemSystem.VisualsChanged` raises `VisualsChangedEvent` to the container owner so the holder re-renders the layer.
- Worn/held layers are added to the *wearer's* `SpriteComponent` as keyed layers (`inhand-left`, or the slot name) and removed on unequip; displacement maps may reshape them for non-human body types.

## 5. Practical checklist for facts you will need
- State exists in `meta.json` **and** as a PNG of matching size/frame count, or the whole RSI fails to load.
- Prototype `sprite:` value must be the RSI path relative to `Textures/`; a missing state prints a client error and shows a placeholder.
- Anything that animates or swaps visuals at runtime = an Appearance key + layer key pair (+ visualizer), not a direct sprite call from shared code.
- Icons in UI (action/alert/verb icons) use the same `{sprite, state}` pair (`SpriteSpecifier.Rsi`).
- License/copyright are mandatory in each `meta.json` for new art (repo uses REUSE headers elsewhere; `Tools/apply_reuse_headers.py` exists).
