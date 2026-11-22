# Sound-path testing

Integration tests live in `Content.IntegrationTests/Tests/_Maid/PathSoundTest.cs`; run with `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --filter "FullyQualifiedName~PathSound"`. They pin the analytic contract of the model: a sealed scene, blockers placed, and the folded filter asserted against an expectation computed by the `Expected()` helper — raw occl (blockers − past-decay) plus the straight-line distance × `path_loss_db_per_tile`/0.8686. The test surface is `TryGetModelOcclusion(listener, sourcePos, out occl, out len, out past, out filter, doorSource, breakdown)` — the same fold the engine write mirrors; `breakdown` returns per-root primary/detour verdicts (occl/hops/past, share, both folded values), `doorSource` emulates a door-own sound.

## Scenes

- `SetupScene(corridorLength)` — a sealed one-tile corridor east of the listener: floor and flanking wall rows span x=−2..len+3, with end-cap walls on the last columns. The mouths are capped — belt and braces since vacuum tiles became unreachable on both passes (they predate that fix, when the primary's vacuum hops leaked clean through an open end into space). Every wall tile has floor under it — walls spawned on missing tiles are invisible to the field (`ComputeTileWeight` early-returns 0 for them).
- `SetupBankScene` — a glass-airlock bank (x=2..5) embedded in the north wall row; sounds are born on bank tiles. Perimeter wall probes (SumW > 50) guard against invisible walls.
- `SetupColumnBankScene` — three glass-airlock columns (x=−3/−1/+1, y=0..5) floor-to-ceiling; the source is the sounding door itself at (−3,0), a closed glass door sits between it and the listener, and the middle column's door opens as a bypass.
- Blockers are placed via `Place(scene, x, proto)`; door states are driven by `SetColumnDoorState`/`SetDoorState`, which set the state and flip collision on BOTH sides.

## Contract matrix (defaults: loss 1.5/tile, decay 0.3/tile, detour weight 20)

- Clean 10-tile corridor ≈ 17 occl units — the pure straight-distance fade.
- One closed solid airlock, a solid+glass vestibule, or five solid airlocks in a row: silence band, filter > 30 — with 0%-transmission doors every route transmits nothing, so the exact path is irrelevant and only the band is asserted.
- A wall in a sealed room ≈ 137 (raw wall occl 120 + straight fade).
- A 1% object (glass airlock or window, occl 4.605) with the source five tiles past: exact ≈ 20 — audible, and never bypassed: `DetourDoesNotBypassAudibleDirectRoute` asserts filter == primaryExpected with detours enabled and disabled.
- A source born on a blocker tile is exempt from its own tile (past 0): pure distance fade (`SourceOwnTileExemption`).
- Sources past the field edge: the source tile is clamped to the edge and the straight remainder folds in (`BeyondFieldRadius`).
- Bank own sounds stay audible (< 12/< 15) — the own-tile price is read from the field window, not the live world (`BankOwnSoundStaysAudible`).
- Detour purity toggle (`DetourPurityDecayToggle`): raw 19.2 / decayed 17.6 ± 3 — the strip gate is held at 90% muffle because the shipped 99% needs a 16-tile walk-out to decay.
- Glass column bank with an open bypass (`GlassColumnBankOpenBypass`): the door's own sound rides the clean bypass route (< 12); a foreign source = analytic blend of the bypass roots; the re-shut bypass muffles (> 8).
- `EntitySoundAudioEntityIsParentedToSource`: a server-side `PlayPvs` on a door reaches the client as an audio entity whose `Transform.ParentUid` is the door — the contract the doorSource detection relies on.

Rough hand bands (defaults): occl < 1 clean; 4.6 = 1% highs (−4 dB broadband); > 10 highs dead, bass following at −0.87 dB per unit; silence contracts assert > 30.

## Design decisions (not bugs)

- The volume fade is straight-line distance only. Route length lives in the route-selection folds (`f1`/`f2`), not in loudness: a clean detour never punishes loudness for hops taken only to dodge blockers whose muffle the occlusion already carries.
- Past-decay is asymmetric by design: the muffle decays over the open walk between the last blocker and the source, so a door adjacent to the listener barely muffles a far source while the same door adjacent to the source muffles fully.
- Root interpolation is 4-corner bilinear anchored at tile edges — a listener at a tile center blends four roots equally. Deliberate smoothness choice.
- Vacuum tiles are unreachable on both passes; a source with no non-vacuum route (space sources) falls back to the raycast. Before the exclusion the primary's free vacuum hops leaked clean around 0% blockers through any open end into space.
- The transmission table (walls and solid doors 0%, glass airlocks/windows/windoors 1%, firelocks 0%/glass firelocks 1%) is the whole truth for audible direct crossings — the detour blend fires only when the direct route transmits ≤ 0.1% (`AudibleDirectCeiling`).

## Knobs

- `AudibleDirectCeiling` 6.9 — the dead-direct threshold (−ln(0.001)).
- `MinDetourExtraHops` 4 / `PureDetourShare` 0.7 / `DetourShare` 0.15 — the detour ladder: a pure detour takes 0.7 at 1 extra hop, an impure one 0.15 at 4 extra hops; both only behind a dead direct route.
- `maidsound.path_purity_decay` — purity measured on past-decayed occlusion instead of raw closed tiles; instant toggle, shown in the overlay legend and snapshots.
- `maidsound.path_detour_weight` 20 — blocker cost multiplier of the detour pass; 0 disables detours.
- `maidsound.path_occlusion_loss_db` 15 — occlusion weight of the route-selection folds only; the filter does not read it.
- Debug: `soundpathsnap [label]` writes a full JSON state dump to the client user data dir (cvars, listener, field, tile weights, per-root breakdowns, per-stream verdicts, a 1024-entry ring of recent decisions) — decode any "I hear / don't hear" report from it; dead streams linger ~10 s. `soundpathdebug` prints the last filter values per sound.

## Integration-test gotchas

- `PhysicsComponent.CanCollide` is a networked delta field: a client-only flip is clobbered by replication. Flip collision on both sides via `SharedPhysicsSystem.SetCanCollide`, and drive door states with `SetState` on both sides plus `NextStateChange = null` (airlocks auto-close). The replicated door state itself never flips client collision — the model follows client physics, a documented contract ("opened but solid on client").
- Teleporting the local mob requires a client-side teleport too (client-authoritative physics); read server-side uids only through `pair.ToClientUid` (reading them via client EntMan throws and dirties the pair); use the aliases `ClientDoorSystem`/`ServerDoorSystem` (one type name, two assemblies).
- Read `MetaDataComponent.EntityName`, not `AudioComponent.FileName`, in client queries — the access analyzer rejects FileName from tests (RA0002).
- Directed event handlers in this RT bind to `EntityEventRefHandler` — a `ref` signature is required even for by-value event payloads; one handler per (component, event) pair client-wide.
- `MapCoordinates` carries no GridUid in this RT — resolve grids from a position via `_map.TryFindGridAt(coords, …)` + `CoordinatesToTile`. `Vector2i.Length` is a property, not a method.
- NUnit truncates long assert messages (~90 chars in the console) — prefer short per-assert probes over one giant dump string; when two tests share an identical scene-setup header, bulk edits by that header hit both — anchor edits on test-unique lines.
- `Expected()` is only for audible routes: 0%-transmission scenes (walls, solid airlocks, stacked glass) assert silence bands (> 30) instead of exact values, because the exact path the model picks through 0% walls is irrelevant and the dead blend returns the 400 ceiling (its exponential underflows to exact zero at the engine — never clamp the exponent at −ln(1e-9): that floors every dead route at −18 dB of broadband bass).
- Analytic expectations must include the past-decay (`occlR = max(0, Σblockers − 0.3·past)`); a source born on a blocker tile has past 0.
- `System.Text.Json` is banned in client content by the sandbox type-check (the snapshot dump uses a hand-rolled `JsonBuilder` with locale-safe float formatting).
- `volatile` fields are rejected by the same sandbox — scratch-buffer flags are plain fields; the worker is the only writer after `PostBuild`, and the frame update is the only adopter.
- A client that dies to `taskkill` does not flush its config — ARCHIVE'd tuning set in such a session is lost.
