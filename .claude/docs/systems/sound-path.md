# Sound path

Tile-based sound propagation for the local player (client-only, `_Maid` folders; the server is not involved). Replaces the engine's single-raycast occlusion with a weighted shortest-path model: how a sound reaches the player depends on the cheapest tile path between them and on the muffling objects that path crosses.

## Pieces

- `Content.Client/_Maid/Audio/PathSound/PathSoundSystem.cs` — the whole mechanism: field building, the occlusion override, the volume post-pass, smoothing, debug state.
- `PathSoundOverlay.cs`, `PathSoundOverlayCommand.cs` (`showsoundpath`), `PathSoundOverlayModeCommand.cs` (`soundpathmode`), `PathSoundDebugCommand.cs` (`soundpathdebug`) — debug tooling.
- `Content.Shared/_Maid/Audio/SoundPathWeightComponent.cs` — networked `muffle` percent (0–100) that turns an entity into a blocker with exact transmission.
- `Content.Shared/_Maid/CVars/CVars.cs` (`MaidCVars`) — all tuning under `maidsound.*`.
- `Resources/Prototypes/_Maid/Audio/sound-weights.partial.yml` — partial prototypes adding the component to upstream windows and airlocks.
- `Resources/Prototypes/_Maid/Entities/Objects/Devices/sound-emitter.yml` — `TestSoundEmitter`, beeps every 5 s for playtesting.

## Fields

Four Dijkstra fields are rebuilt over grid tiles around the local player (radius from `maidsound.path_radius`, clamped 4–32). The four roots are the tiles around the player's continuous position; receiver-side bilinear interpolation between roots keeps values continuous across tile crossings (a deliberate design decision — one root would be simpler but loses interpolation).

Cost of entering a tile is `1 + tileOcclusion`, from the anchored entities:

- an entity with `SoundPathWeightComponent` contributes `OcclFromMuffle` (`-ln(1 - muffle/100)`, so `muffle: 90` leaves a tenth of the highs) while its physics body `CanCollide` is true — open or destroyed doors drop out, welded ones stay;
- otherwise a hard fixture on `Impassable` counts as a wall (`path_occlusion_per_weight * weight_opaque`, or `weight_glass` on `GlassLayer`); fixture layers exactly equal to `AirlockLayer`/`GlassAirlockLayer` count too — airlock fixtures carry no `Impassable` bit (`AirlockLayer = Opaque | GlassAirlockLayer`), so the vanilla occlusion raycast never sees airlocks at all.

Component objects are absolute (percents, independent of cvars); heuristic objects scale with cvars. Rebuilds happen when the player's base tile, grid or radius changes, when a broadcast blocker event fires — `CollisionChangeEvent` covers every `CanCollide` flip including the mid-closing moment when a door becomes solid (which fires no door-state event), `DoorStateChangedEvent` covers door open/close, `AnchorStateChangedEvent` and `TileChangedEvent` cover built, broken and re-anchored blockers; all event rebuilds are throttled to one per 50 ms — or when `maidsound.path_cache_ttl` (2 s) expires as the fallback for anything without an event. Tile weights are cached per rebuild.

## Per-sound values

`TryGetAudio` computes, per root, the summed occlusion along the cheapest path to the source tile. A source standing on a blocker tile is muffled by that blocker like any other — except while its tile is the player's tile or adjacent to it (Chebyshev ≤ 1), where the destination tile's own occlusion is subtracted so a door at arm's length does not muffle itself. Per-root results blend in the gain domain: cutoffs `e^-occl` are averaged across roots and the returned occlusion is `-ln(blended cutoff)`; volume changes blend as dB. Blending gains instead of raw occlusion turns a route disagreement (two symmetric detours around a diagonal wall) into a fade instead of a silence/clean toggle.

Volume fades over the full path: `path_loss_db_per_tile` per tile of path length — straight distance included, so far sources are quiet, and past-blocker tiles are counted once more on top of their own path length, which steepens the fade behind a choke point. The muffle itself also costs loudness: `path_occlusion_loss_db` (4 dB per unit of applied occlusion, ~9 dB for a 90% airlock) on top of the lowpass, since a lowpassed sound at full gain is still loud. Each field also tracks the path tiles past the last blocker (`_past`, reset to zero on every blocker tile): the occlusion decays by `path_occlusion_decay_per_tile` per past tile, so the flat muffle zone behind a single choke point becomes a gradient. Both past-tile terms apply only when the path actually crossed a blocker (`sumW > 0`). Applied volume floors 60 dB below the sound's own volume — practical silence without `-inf`.

## Write paths

Occlusion goes through the public engine hook `AudioSystem.GetOcclusionOverride` — a single-target event invoked per source from the engine's parallel audio job at `audio.tick_rate` (30). The override returns the smoothed field value for sounds inside the field. Beyond the field radius it reads the field at the edge tile along the listener→source ray (the occlusion accumulated on the way) and adds an `IntersectRayPenetration` raycast over only the remaining stretch, with the ray mask widened by `GlassAirlockLayer`: airlock fixtures carry no `Impassable` bit, so the vanilla mask would let far sources play clean through any number of airlocks. Only when no usable field exists at all does the raycast cover the whole way. Occlusion smoothing keeps one slot per quantized source position (same-position sounds share it).

Volume is written by the system's own post-pass, ordered after the engine `AudioSystem` via `UpdatesAfter`, with the accumulator interval synced to `audio.tick_rate` and the remainder preserved: the engine resets `AudioComponent.Volume` from `Params` every audio tick, so both sides must tick in lockstep or the volume pumps. The post-pass glides each managed sound toward its target (`maidsound.path_smoothing` fraction per tick) and sweeps its per-uid state lazily. Sources are managed up to `maidsound.path_volume_range` (40 tiles). Beyond the field radius the path is read at the field edge along the listener→source ray — the occlusion accumulated on the way plus straight-line loss for the remaining stretch — and the occlusion override adds a raycast for only that remaining stretch, so the ray no longer flickers whenever a mob crosses the much longer line to the source. Sources with no usable field at all fall back to distance-only loss. Management is sticky: a sound stays managed until it leaves `path_volume_range` + 4 tiles, and `path_max_sources` throttles only newly managed sources — a strict rank cap would flicker boundary sounds in and out, pumping between the attenuated volume and the engine's per-tick reset to full volume. A newly managed sound glides in from its current volume instead of stepping straight to the target. Marker labels show the applied (glided) volume delta, since the raw per-tick target snaps instantly on world changes while the audible glide follows over a few ticks. Non-finite values never enter the smoothing state: `AudioComponent.Volume` reads back through `GainToVolume`, which is `-inf` for a zeroed (stopped or NaN-guarded) source, so targets, glides and stored state are all finite-checked and poisoned values are skipped or swept. Unreachable field tiles (sealed rooms) are skipped in the per-root blend instead of contributing a fake-clean zero. It never writes `Occlusion` — the override is the only occlusion writer.

Smoothing exists because `BaseAudioSource.Occlusion` reissues the OpenAL EFX filter calls on every write even when the value is unchanged: two writers at different values per tick are audible as pulsing. `AudioFlags.NoOcclusion` is never used — an earlier bypass that set it made the engine write `Occlusion = 0` before the system's value each tick.

## Invariants and failure modes

- A directed `(component, event)` subscription pair allows exactly one handler per client (`EntityEventBus` throws on duplicates), and a failed connection attempt re-runs system `Initialize` on the next join — so this system holds no directed subscriptions; the volume-smoothing set is swept lazily instead of via `ComponentShutdown`.
- Broadcast subscriptions to one event must all agree on by-ref or by-value (`EntityEventBus` throws on a mix during join-time re-initialization, which fails the join). Engine struct events this system listens to — `TileChangedEvent`, `AnchorStateChangedEvent`, `CollisionChangeEvent` — are by-ref and keep `ref` handlers; class events like `DoorStateChangedEvent` are by-value.
- The engine clips audible distance by euclidean `MaxDistance` before any of this runs: path-based audibility cannot extend hearing range (that would need `ProcessStreamOverride`, which is single-target and takes over the whole stream update).
- Cross-grid sounds are skipped by the post-pass and fall back to the raycast in the override.
- Occlusion smoothing slots are touched from the engine's parallel job threads; races are benign (worst case two positions share a slot for one tick).
- World changes while standing still are picked up by the blocker-event rebuilds; the TTL rebuild is only the fallback for events that never fire (direct `CanCollide` toggles outside the door system).

## Debug

`showsoundpath` toggles the overlay; `soundpathmode <off|field|numbers|all>` picks what is drawn: tiles colored by audibility `S = e^-sumOccl * 10^(-lossDb/20)` including both fade terms (green ≥ 0.7 clean, yellow muffled, red barely, grey no path), per-tile percentages, managed-sound markers with their applied values (marker labels show the applied, glided path volume delta — not the raw per-tick target and not the sound's own base volume), and a legend with all tuning values. `soundpathdebug` dumps the last post-pass snapshot per managed sound. `maidsound.path_smoothing 1` disables smoothing for A/B comparison; `maidsound.path_enabled false` reverts to engine behavior entirely.
