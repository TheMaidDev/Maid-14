# Localization

How fluent localization works in this fork, why it spams the console, and how to audit/fix it. Covers content-side issues only; `RobustToolbox/` is never modified for these.

## How lookup works

`LocalizationManager.GetString` (engine, read-only) logs `Warning: Unknown messageId ({culture}): {key}` when a key is missing and **returns the input string unchanged**. Correct-looking text with console spam therefore means a failed lookup feeding already-valid text back. Lookup order: default culture (en-US) → fallback cultures (ru-RU). A key missing only in ru-RU never logs for en clients and vice versa, but a key missing everywhere logs for everyone.

Entity names/descriptions are special (`LocalizationManager.Entity.cs`): `EntityPrototype.Name`/`.Description` are **already localized** via `ent-{id}` fluent messages, falling back to the raw YAML `name:`/`description:` strings. The engine resolves `.desc`/`.suffix` attributes per parent chain and logs `No value: ent-X.desc` when an `ent-X` message exists with attributes but no `.desc`.

## Bug classes that spam `Unknown messageId`

- **Double localization**: passing an already-localized string (`EntityPrototype.Name`, `JobPrototype.LocalizedName`, `MetaData(entity).EntityName`, a `GetString` result) into `GetString` again. Every lookup fails and logs. Fixed sites are marked `// MAID fix double localization`.
- **Missing keys**: code references a key absent from en-US (default culture) — the largest spam source. Past examples: `chat-manager-language-prefix` (called for every visible-language chat/radio message; the key was deliberately commented out in `en-US/chat/managers/chat-manager.ftl`, so the code was changed to pass an empty display instead), fork content keys that existed only in ru-RU (botany analyzer, offer-item, _Maid markings).
- **Key typos**: wrong key text never resolves anywhere — `cmd-rmojective-*` (typo), `cmd-dungen_pack_vis` (ftl has `-success` variant), a trailing space inside a key literal (`"space-villain-game-enemy-dies-with-player-message "`). Fix code to the existing key; only add ftl when no key exists.
- **Raw human text as key**: `Loc.GetString("Wrong lavaland prototype!")` — replace with a real key plus ftl entries.
- **Prototype `name:` raw strings** (Pattern B from PRs #248/#254): prototype YAML `name:` with human text consumed by `Loc.GetString(proto.Name)` — point the prototype at a kebab-case key and add it to `Resources/Locale/{en-US,ru-RU}/_Maid/`.

## False alarms (verified against Linguini 0.8.0 via a test harness)

- `{ "" }` placeables (~40 across locales, mostly the ss14-ru translation pack): format to an empty string with **zero errors**; intentional "empty" markers. Do not mass-edit.
- ftl lines that look empty (`key =`): usually multi-line values (continuation on indented lines) or intentional empty descriptions of toolshed commands.
- YAML `description:` bare-empty or `''` in entity prototypes: raw fallback strings, never logged. `suffix: ""` is not a localized field.
- Keys missing only in ru-RU produce untranslated text, not console errors.

## Audit method

A full sweep checks four things (scripts live outside the repo, `D:/maid-loc-audit/`):

1. Prototype keys vs ftl: for every watched prototype doc type, extract `name:`/`code:` and verify the value exists as a fluent id in en-US **and** ru-RU, plus `marking-{id}` for markings.
2. Empty values: ftl entries whose value is truly empty (no same-line value and no indented continuation) and YAML `name:`/`description:` that are quoted-empty.
3. Code literal keys: extract every string literal passed as first arg to `GetString`/`TryGetString` and check membership in the en-US+engine (RobustToolbox locale) and ru-RU id sets. Concat prefixes (`"armor-damage-type-" + x`) need their runtime suffixes checked separately.
4. Double-lookup grep: `Loc.GetString(.*LocalizedName|EntityName|entProto.Name)`, nested `GetString(GetString(...))`, `TryGetString` immediately followed by `GetString`.

`ftl` files take no comments (AGENTS.md); fixes outside `_Maid/` use MAID markers; new keys for fork content mirror the ru file layout under `Resources/Locale/en-US/_Maid/`.

## Sweep status (2026-11)

Swept clean: client menus listing prototype names (antag list, emotes, bar signs, lathe/store categories, TTS voices, ID console, guidebook entries, cargo accounts/bounties), all entity/name double-lookups found by the greps above, nested `GetString(GetString())` in server commands, and the four audit checks. Remaining flagged items are benign: non-consumed tile `name:` fields (never passed through `GetString`), `{ "" }` markers, and concat prefixes whose suffixes exist.
