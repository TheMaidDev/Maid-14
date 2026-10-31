# Systems

Entry point for documentation of individual content systems (gameplay systems such as interaction, inventory, damage, shuttles). Each documented system gets `systems/<system>.md`; a system with sub-aspects gets a hub file plus children in `systems/<system>/`. Engine-level mechanics (how systems, events and components work) are in `ecs.md` and `ecs/`.

- `systems/localization.md`: when `Loc.GetString` receives already-translated text (Unknown messageId spam), which network fields are IDs vs final text, and known open instances.
