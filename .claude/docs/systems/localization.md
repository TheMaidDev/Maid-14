# Localization (Loc) pitfalls

Prototype fields such as job `name`, role `name`, department `name` hold **loc IDs** (e.g. `job-name-captain`), not text. `Loc.GetString` turns an ID into text exactly once. Text is Russian (ru-RU) in production, so an ID lookup of already-translated text fails.

## Double localization
Passing an already-localized string to `Loc.GetString` logs `Unknown messageId ("ru-RU"): "<translated text>"`. The `.ftl` dictionaries are not at fault. Hot paths (overlays, per-frame draws) turn this into continuous log spam.

Rule: decide per field whether it carries an ID or final text, and localize only IDs. Server-side code usually ships final text to clients (the server localizes with its own culture), so a client receiving a `string` field from a network message/state should assume it is final text unless the field is documented as an ID.

## Known fields that are already final text
- `PlayerInfo.StartingJob` (admin player list): filled via the job system's `MindTryGetJobName`, which returns `LocalizedName` or a localized "unknown" fallback. Fixed in the admin name overlay; the player tab already used it raw.
- Station record `JobTitle`: set from the job's `LocalizedName`; the ID console can also store free text.
- Round-end player info `Role` fallback (`game-ticker-unknown-role`) is localized by the server, while real job/antag roles there are IDs, so the field is mixed.
- Ghost warp panel job names: localized on the server, shown raw on the client (correct).

## Open instances (not fixed yet)
- Client station record view (`GeneralRecord`) calls `Loc.GetString` on `JobTitle` (rebuilt on UI refresh).
- Round-end summary window calls `Loc.GetString` on the player's `Role`; only the "unknown role" fallback is affected.

For mixed fields prefer `Loc.TryGetString(x, out var s) ? s : x`, or change the sender to ship an ID consistently.
