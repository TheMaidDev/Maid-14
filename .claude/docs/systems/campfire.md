# Campfire cooking

A bonfire (`Bonfire` and its children `LegionnaireBonfire`, `RMCCampfire`) is a fuelled fire, not a permanent heat source: it starts unlit and empty, has to be fed and lit by hand, and only while burning heats items lying on it, so existing temperature-based recipes cook food over it. Code lives in the `_Maid` folders of Shared and Server (`Campfire`).

## Pieces
- `CampfireComponent` holds remaining burn time, a cap on it, the materials that count as fuel with burn seconds per material unit, heat power per item, a temperature ceiling for heated items, the pickup radius, the item limit and the tick interval. The lit flag and a coarse fuel level (empty/low/medium/high) are networked; exact fuel stays server-side.
- The prototype also carries `PlaceableSurface` (items placed on it land in the centre) and a `GenericVisualizer` that shows the fire layer only while lit. The old `AlwaysHot` is gone from the bonfire; the system answers the "is hot" query itself while lit, so a burning fire still lights cigarettes and torches. Light and ambient fire sound start disabled and follow the lit state.

## Flow
- Using an item on the fire: if unlit and the item is hot (lighter, welder, lit match, anything answering the hot query), the fire lights provided it has fuel (otherwise a "nothing to burn" popup). Otherwise, if the item's physical composition contains a fuel material (wood, cloth, paper), burn time is added and one unit is consumed (one piece of a stack, or the whole item); a full fire refuses. Anything else falls through, so the item is placed on the fire.
- Fuel value is sum over composition of material amount times the configured seconds per unit; a wooden plank is a minute. This reuses material compositions, so any wooden or cloth object works without per-prototype tagging.
- Once per interval a lit fire burns fuel and heats loose items (item component plus temperature component) within the radius, up to the item limit, by adding heat energy until they reach the ceiling. Cooking itself is not new: construction graph steps with a minimum temperature fire on temperature change, using internal temperature when present. Running out of fuel puts the fire out. An alternative verb puts a lit fire out by hand.
- Mobs are never heated; only items.

## Tuning notes
- Heat power equals the electric grill. Surroundings matter: items exchange heat with the air, so in vacuum or very cold air the fire's steady-state temperature can stay under the cooking thresholds; on the station it clears all of them.
- Not covered: putting the fire out with water/extinguishers, burning mobs or items thrown in, fuel-dependent heat strength.

## Tests
- `CampfireTest` (integration, `_Maid`): feeding, lighting, cooking raw meat, burning out, and an unlit fire not cooking.
