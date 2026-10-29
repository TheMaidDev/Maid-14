# Antag extraction (drop pod)

Generic way for an antagonist with objectives to leave the round early. SpaceNinja is the first user, the system itself knows nothing about ninjas. Everything lives in `_Maid/Extraction` (Shared, Server, Client) plus prototypes/locale/textures under the matching `_Maid` folders. Branch: `ninja-extraction` on `origin` (TheMaidDev/Maid-14).

## Player flow

1. **Unlock.** `AntagExtractableComponent` on the antag body. Every `checkInterval` the server counts the mind's objectives:
   - `requiredObjectives` must all be completed; `optionalObjectives` count toward the threshold. Objectives in neither list are ignored. If both lists are empty, every objective except survive-type ones counts.
   - `thresholdMode` Percent or Count, `threshold` value. No counted objectives at all → never unlocks.
   - On unlock the player gets a random pod code (`codeLength` digits, 1–9) in chat.
   - Admins: `extraction:unlock <id>` (adds the component if missing, counts current inventory as gear) and `extraction:reset <id>`.
2. **Device.** A handheld "device of unknown purpose" (`ExtractionDevice`) is handed out once, one tick after the component appears (so starting gear is already equipped): backpack → free pocket → hand. Marked as antag gear. Only its owner can open it; anyone else gets a single "we don't recognise you" popup (use-in-hand is intercepted before ActivatableUI, otherwise the engine falls back to activation and the popup shows twice). The device UI replaced the old action buttons; the panel had to stay free of actions.
   - Shows: done/not done status, progress line, per-objective list, zone locations, pod status (flying / landed, leaves in M:SS / launching / no longer available), zone re-assignments left, access code or "not received yet". Buttons: Refresh, Mark extraction zone, Prepare pod (enabled only inside a zone).
   - UI is server-built state; texts depending on server data come localized.
3. **Zones.** "Mark extraction zone" spawns `zoneCount` (3) zone markers on the station's main grid. `maxRequests` = first request + 1 re-roll; a re-roll replaces all zones and prefers anchors different from the current ones. Only the admin reset gives more.
   - Station grid = largest member grid (by bounding box, upstream `GetLargestGrid`) of stations with `StationEventEligible` (excludes CentComm/outposts).
   - Placement order: entities with components from `zoneAnchorComponents` in order (Holopad, then NavMapBeacon), minus `zoneAnchorBlacklist` (AI core holopad); missing spots are filled with random free pressurised tiles (bounded sampling), for which the player gets map coordinates.
   - Zone = 3×3 square (`range` 1 on the marker). Visible only to the owner (force-sent via PVS session override) and to admin ghosts (visibility bit `MaidExtraction`, added for ghosts of admins). Losing some zones does nothing; when all are gone a new set is given for free.
4. **Calling the pod.** Allowed exactly once, only while standing in one of the zones. After pressing, the player may walk away: the pod lands on the tile the call was made from after `podFlightTime` (15 s). Station announcement with the landing location after a random 5–20 s. If the grid is gone by landing time the call is still spent (only admin reset gives another).
5. **Landing.** Only living, uncontained mobs on the tile are gibbed (bodies) or destroyed (body-less mobs); objects are untouched. Impact sound like meteors/explosions: positional near sound in range, global far sound for the rest of the map. Screen shake fades with distance. Rubble decal (64×64, offset to sit under the pod) stays even if the pod is gone. The pod plays a one-off landing animation on clients that saw the landing (door layer kept hidden through appearance updates until it ends).
6. **Pod.** Standalone entity storage (not a closet child, not destructible, no weld/anchor/pull). Locked until the code is entered on its keypad (opening a locked pod brings up the keypad). Only living creatures can be inside; items are refused at insertion. PointLight colour shows state: red locked, green unlocked, blue sealed; yellow only on the lift-off copy. Glow sprite layers were removed on request, the light stays.
7. **Launch.** Extractee inside presses the START action → pod seals for `launchDelay` (15 s, countdown popups; a 15 s engine startup sound plays and stops if the launch is cancelled; animated welding-spark sprites (the Goob welder effect, `welding_effect.rsi`; Goob's particle emitter showed nothing in game even with a client-side emitter, so it was dropped) appear around the pod for the last `launchSparksSeconds` (5) and are removed on cancel or lift-off; optional per-second countdown sound, empty by default). Leaving the pod cancels the launch. A landed pod leaves on its own after `autoLaunchDelay` (5 min, warning 1 min before) with or without the extractee.
8. **Leaving.** Others inside are ejected onto the station. With `leaveStationItems` (off by default, on for ninja) station items drop and own gear (marked at antag selection) leaves; carried people always stay. The player becomes a ghost that cannot return; the mind keeps owning the living body, which is moved to a paused "extraction limbo" map. That keeps kill objectives against them uncompleted and survive objectives completed (survive also has a small MAID check for extracted minds). Round end summary gets an "evacuated N minutes after arrival" line.

## Configuration surface

- `AntagExtractableComponent`: thresholds and objective lists, zone count/requests/anchors/blacklist, device prototype, code length, pod flight time, announcement delay range, `landing` block (pod prototype, crush living, impact sounds and near range, shake range/strength), `leaveStationItems`, `messages` block (every chat/announcement/round-end text as a LocId; defaults are neutral, an antag may override).
- `DropPodComponent`: launch delay, countdown popup seconds, auto-launch delay/warning, launch animation length/sprite/offset, decal and its offset, light colours, sounds, pod texts.
- `ExtractionZoneComponent`: zone size.
- Ninja config sits in `NinjaSpawn` (events.yml) and in the Goob token rule and ninja implant, all with `# MAID` markers. Ninja throwing stars carry the gear marker in their prototype.

## Decisions worth keeping

- Generic system, neutral texts; no "clan" flavour in defaults (removed on request).
- One pod per antag, no second chance except admin reset. Zones: one re-roll only.
- Extractee standing on the landing tile gets gibbed and the pod stays locked forever — accepted.
- Dead extractee inside the pod still gets extracted — accepted.
- A cuffed/stunned extractee pushed into an unlocked pod can still launch — intended.
- Pod destructibility postponed; currently indestructible.
- Device is not a PDA cartridge: spawn-in antags carry an agent ID, not a PDA, and a PDA would expose them on station messaging.

## Open / deferred

- Extracted bodies on the limbo map still count as living antags for dynamic/adaptive game mode tallies. TODO on the limbo move; exclude the map later.
- Mind/body mismatch (clone, body swap): the extraction stays on the body, another player could use it. Deliberately left for the final pass.
- Admin ghosts don't refresh the extraction visibility bit on de-admin/re-admin until a new ghost.
- Sprite source of the device (`suspiciousphone`) not confirmed; pod and rubble sprites are from BeeStation `supplypods.dmi` (CC-BY-SA 3.0).
