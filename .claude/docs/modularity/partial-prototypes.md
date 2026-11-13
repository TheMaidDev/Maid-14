# Partial prototypes, abstract files, variants

Engine feature of `PrototypeManager` (added in RT 289.0.0; `!CombineIndex` and several merge/hot-reload fixes in 289.0.1-289.0.2). It lets a later YAML file modify a prototype defined elsewhere instead of redefining it.

Full behavioural spec (merge algorithm, tag positions, quirks): [partial-prototypes/spec.md](partial-prototypes/spec.md).

## Enabling
- Partial treatment is per path, set from C#: `IPrototypeManager.PartialDirectory(ResPath dir, int index)` and `PartialFile(file, index)`. Paths are VFS paths without `Resources` (e.g. `/Prototypes/_Maid`). The call is not retroactive and must happen before the directories are loaded; Content's `Init` phase is early enough.
- Maid registration: `Content.Shared` `EntryPoint.Init` calls `MarkPartialPrototypes`, which delegates to `MaidPartialPrototypes.Mark` (`Content.Shared/_Maid/Prototypes`). It registers every directory in `MaidPartialPrototypes.PartialDirectories` (relative to `/Prototypes`; currently `_Maid/Override`) at index 0, and every content file named `*.partial.yml` (index 0) or `*.partial.N.yml` (index N; a non-number or negative N throws `PrototypeLoadException`). `MaidPartialPrototypes.IsPartial` answers the same question for a single path.
- `/Prototypes/_Maid/Override` mirrors the upstream file layout: a partial for a prototype from `/Prototypes/<path>.yml` lives in `/Prototypes/_Maid/Override/<path>.yml`, every entry typed `!PartialOnly`. The out-of-repo `asset-override` tool (Bun/TypeScript) generates these entries to repoint upstream prototypes at old-build assets copied to `<kind dir>/_Maid/Override/<same path>`, merging into override files that already exist.
- Inside a partial path, a prototype whose id already exists is merged into it; without a partial path the same situation throws `Duplicate ID`. A partial prototype whose id does not exist simply becomes a new prototype, unless its type node is `!PartialOnly <kind>`, in which case it is dropped.
- Works for every prototype kind, not only entities.

## Ordering
- All non-partial files load first. Partial files are queued by their `index` (smaller first); inside one index they are ordered by file path (ordinal). Two partials touching the same field therefore apply in a deterministic order. A fork directory that should win gets the larger index.
- Hot reload re-applies the stored original plus all partials of that id in order. Only the raw YAML is merged; inheritance resolution (`parent:`) and validation happen afterwards on the merged result. A partial without its own `parent:` does not touch the inheritance graph.

## Merge rules (tree merge of raw data nodes)
- Mapping field present in the partial: scalars replace; nested mappings merge recursively; sequences get the partial's elements added to the existing sequence.
- **Entity `components:`** is a sequence that is treated as a dictionary keyed by `type`: a component in the partial is merged field by field into the existing component of the same type; if absent it is added.
- Tags (exact spelling):
  - `!Remove` as a value or key tag deletes that key (`"a": !Remove` always; `"a": !Remove 1` only if the current value equals 1); on a sequence element it removes the equal element; `- !Remove type: Foo` removes component `Foo`. A mapping left empty by removals is deleted entirely; a lone `!Remove` entry in a sequence mapping that finds nothing is not added.
  - `!Clear` empties the existing mapping/sequence first; elements listed next to it are then added.
  - `!Index:N` on a sequence element inserts it at position N (negative counts from the end; out of range is clamped).
  - `!CombineIndex:N` merges a mapping element into the existing element at N instead of adding (required since RT 289 to modify an existing list element).
  - `!PartialOnly` goes in the `type:` node of the prototype.
- Removing or clearing leaves an internal marker (`!PartialModified`) so the component serializer does not complain about a component missing its `type` key; it is an engine detail, never written by hand.
- Per-step trace is available with the PrototypeManager sawmill at verbose level.

## Validation
- `ValidateDirectory(path, partialNoOriginalError)` understands partials. With `true` it errors on a non-`!PartialOnly` partial with no original; useful if partials and plain definitions are not meant to mix.
- It loses key tags (spec §7), so a `!CombineIndex` element is appended during validation and fails as an incomplete entry or crashes a validator. `Content.YAMLLinter` therefore does not validate `/Prototypes` directly: `CombineIndexValidation.ValidatePrototypes` (`Content.YAMLLinter/_Maid`) copies all `/Prototypes` `.yml` files into a `MemoryContentRoot` mounted at `/_MaidLinter`, removes `!CombineIndex` elements from partial files in the copy (YamlDotNet's node model keeps key tags), registers the copy's partials through `MaidPartialPrototypes.Mark`, validates it, and maps error paths back to `/Prototypes`. The removed elements are still exercised by the linter's client/server startup, which loads prototypes with the correct parser and fails on logged errors.

## Abstract file list
- `Resources/IgnoredPrototypes/*.yml`: each file is a YAML list of VFS paths. `Content.Shared`'s `Init` reads every file there; an entry without extension becomes `AbstractDirectory`, otherwise `AbstractFile`. Every prototype in those files/directories gets `abstract: true` forced. Inheritance still works, instances and spawn menu entries disappear, other prototypes or code that reference the id by `ProtoId`/`EntProtoId` still need cleaning. The shipped file contains only comments.
- Use for turning off upstream content without deleting upstream files (keeps merges with upstream clean).
- Separate from this, `RegisterIgnore("kind")` calls in the server/client entry points make the loader skip whole prototype kinds that the other side has no class for.

## Variants
- `id: !type:CreateVariants` (and the same tag on nested values) with a `values:` list makes the engine expand one entry into several prototypes, one per value, and can be combined with `!PartialOnly` to patch several prototypes with one entry (e.g. clearing `conditions` for three vendor listings).
