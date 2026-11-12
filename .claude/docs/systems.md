# Systems

Entry point for documentation of individual content systems (gameplay systems such as interaction, inventory, damage, shuttles). Each documented system gets `systems/<system>.md`; a system with sub-aspects gets a hub file plus children in `systems/<system>/`. Engine-level mechanics (how systems, events and components work) are in `ecs.md` and `ecs/`.

- [bush-disguise.md](systems/bush-disguise.md): picking up a potted plant to become it (hand-held two-handed item, hidden player plus visual entity, identity override, mob-like plant physics).
- [discord-feed.md](systems/discord-feed.md): admin chat and new-player events copied to Discord channels via webhooks, plus operator setup (CVars, where the webhook URL goes).
- [campfire.md](systems/campfire.md): bonfires as fuelled fires (feed with wood/cloth, light with a hot item) that heat items lying on them so temperature-based recipes cook food.
- [localization.md](systems/localization.md): fluent lookup mechanics and console-spam bug classes (double localization, missing keys, key typos), false alarms like `{ "" }` placeables, audit method, and the current sweep status.
