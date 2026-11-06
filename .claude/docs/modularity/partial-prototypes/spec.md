# Partial prototypes: behaviour specification

Exact behaviour of the engine's partial-prototype merge (`PrototypeManager`, RT v294.0.1), read from `PrototypeManager.YamlLoad.cs` / `YamlValidate.cs` and the data-node classes. Nothing here was executed; items marked *quirk* look unintended but are what the code does. Overview and Maid-side context: [../partial-prototypes.md](../partial-prototypes.md).

## 1. Selecting partial files
- Partial mode is a property of a **file path**, never of YAML content. C# registers it: `PartialDirectory(dir, index)` and `PartialFile(file, index)`, before loading; not retroactive. Paths are VFS paths without `Resources` (`/Prototypes/...`).
- A file is partial if it is under a registered file or directory (`TryRelativeTo`); files are checked first, then directories; first match supplies the `index`.
- Mode is per file: every prototype in a partial file is partial. A file cannot mix partial and ordinary prototypes.
- `IgnoredPrototypes` (abstract lists) is independent of this and is registered the same way, by path.

## 2. Load pipeline (`LoadDirectory`)
1. All `.yml` files below the directory (names starting with `.` skipped) are parsed in parallel into extracted prototypes: kind, id, parents, raw mapping, `PartialOnly` flag, variant data.
2. Non-partial files are merged immediately via `MergeMapping(partial: false)`. An id that already exists throws `Duplicate ID` (caught per file and logged as an error; that prototype is not loaded).
3. Partial files are queued by `index`. After all non-partial files: queues run in ascending index; inside one index files are ordered by canonical path (ordinal). Each prototype goes through `MergeMapping(partial: true)`.
4. Exceptions inside a partial merge are logged per file and loading continues.

## 3. What happens to one partial prototype
- Original exists: the first partial for an id stores a copy of the original (`PartialOriginals`) and every partial is appended to `Partials[id]`; then the partial's data is merged into the original raw mapping (section 4). If the merge deletes everything (`fullDeleted` and the result is empty) the prototype is removed.
- No original, not `!PartialOnly`: stored as a normal new prototype.
- No original, `!PartialOnly`: dropped silently.
- Inheritance graph: if the partial has `parent`, `inheritance.Add(id, parents)` is called; without `parent`, a partial for an existing prototype leaves the graph untouched.
- Merging is on raw YAML nodes; `parent:` resolution and field deserialisation happen afterwards on the merged result.
- `CreateVariants` entries expand before merging; the `PartialOnly` flag is copied to every variant. Each variant is then merged as its own prototype.

## 4. Merge algorithm
`CombineMapNode(existing, data)` runs for each key of the partial, in order:
1. **Clear**: key tag or value tag is `!Clear`: if the existing value is a mapping or sequence it is emptied (and marked `!PartialModified`). Processing continues, so content next to the tag is added afterwards.
2. **Remove**: key tag or value tag is `!Remove`. If the partial value is empty, or equals the existing value, the key is deleted; a mapping left empty sets `fullDeleted`. Always `continue` (a key cannot be removed and set in one entry).
3. **Existing key**: `Combine(existing, data)`: mapping+mapping recurses; sequence+sequence goes to `CombineSeqNode`; anything else returns false and the value is replaced (only if the new value is non-empty).
4. **New key**: added if non-empty.

`CombineSeqNode(existing, data, processAsMappingKey)` walks the partial's elements **from last to first**. Per element, first match wins:
1. `!CombineIndex:N` (node tag exactly `!CombineIndex:` fails; works only via tag on the **first key** of a mapping element): merge into `existing[N]` via `Combine`. If that index is missing or not combinable it is logged (verbose) and the element is lost.
2. node tag `!Remove`: `existing.Remove(element)`, i.e. removes the first element **equal** to it. Scalars compare by value only (`ValueDataNode.Equals`); mappings compare children **and tag**, so a `!Remove` mapping never equals an untagged existing mapping (no effect).
3. node tag starts with `!Index:` (case-sensitive): insert at index.
4. When `processAsMappingKey` is set (only entity `components`, key `type`): element must be a non-empty mapping with a scalar `type`, otherwise it is skipped. If an existing element has the same `type`, `CombineMapNode` merges into it (a resulting empty mapping removes that element); if none matches, falls through to 5.
5. Otherwise added at the end, except a mapping with exactly one key whose key tag is `!Remove`, which is dropped.

Index parsing: `N` or `-N`; negative counts from the end (`Count - N`); result clamped to `0..Count`; a non-number throws `PrototypeLoadException`.

## 5. Tag table
Tags are exact for `!Remove`, `!Clear`, `!PartialOnly` (case-insensitive equality); `!Index:` / `!CombineIndex:` are prefix checks. One YAML node holds one tag. In `- !T k: v` the tag belongs to the **key**; in `k: !T v` to the value; on its own line above a block it belongs to the block (mapping/sequence).

| Tag | Where it is read | Effect |
|---|---|---|
| `!PartialOnly` | value of `type:` | partial is ignored when no original exists |
| `!Remove` | key tag or value tag in a mapping | delete key (empty value: always; else if equal) |
| `!Remove` | node tag of a sequence element | remove first equal element (scalars) |
| `!Remove` | key tag of the only key of an element | element is not added; in `components` it also removes the matched component |
| `!Clear` | key tag or value tag in a mapping | empty the existing mapping/sequence, then add |
| `!Index:N` | node tag of a sequence element (scalar, or mapping with tag on its own line) | insert at N |
| `!CombineIndex:N` | tag of the first key of a mapping element, same line | merge into element N |
| `!PartialModified` | engine-internal | marks components touched by remove/clear |

## 6. Valid examples
```yaml
- type: !PartialOnly entity
  id: Foo
  name: New name                 # scalar replaced
  components:
  - type: Sprite                 # merged into existing Sprite, else appended
    drawdepth: Mobs
    layers: !Clear               # emptied, then filled
    - state: a
  - !Remove type: Light          # component removed
  - type: Item
    size: !Remove                # field removed
    tags:
    - !Remove Old                # scalar element removed
    - !Index:0 New               # inserted first
    - !CombineIndex:1 a: 5       # merged into element 1
```

## 7. Validation (`ValidateDirectory`)
Ordinary files are checked for duplicate ids and id characters. Partial files are queued per index, merged into the collected originals, and `type` is stripped again. A partial with no original is skipped; with `partialNoOriginalError: true` it is an error unless `!PartialOnly`. `Content.YAMLLinter` uses the default (`false`).

Unlike loading (`DataNodeParser`, which calls `SetKeyTag`), validation parses files with `YamlStream` and converts them through the `MappingDataNode(YamlMappingNode)` constructor, which in the pinned RT records no key tags. Every rule above that reads a key tag (`!CombineIndex:N`, key-tag `!Remove`/`!Clear`) is therefore inert during validation: a `!CombineIndex` element falls through to "add at the end", and the appended fragment then fails validation (e.g. `ComponentRegistrySerializer` throws `KeyNotFoundException` for a missing `type`, polymorphic fields throw for a missing `!type:`). Node tags survive. Maid's linter workaround is in [../partial-prototypes.md](../partial-prototypes.md#validation).

## 8. Hot reload
`LoadFile` / `LoadFromStream` re-run the merge with the partial flag. When `changed` is supplied and stored partials exist for an id, the stored original is restored and all other recorded partials are re-applied in their recorded order before the changed file's one.

## 9. Quirks and limits
- Appending to an existing sequence walks the partial's list last-to-first and calls `Add`, so plain elements appear in **reverse order** after the existing ones (read from the code; RT's own partial tests compare with `Is.EquivalentTo`, so order is untested). A new key is stored as given.
- Because of the last-to-first walk, replacing scalar element N is written as `- !Index:N new` followed by `- !Remove old`: the remove runs first, then the insert. With such pairs in ascending N, each index is rewritten while all lower indices still hold their original elements; duplicates of the same old value are handled because each removal takes the first equal element and all of them are replaced.
- `!CombineIndex` on its own line and `!Index` with the tag on a key are not recognised; the element is appended instead.
- Mapping elements outside `components` cannot be removed by tag; rewrite the list with `!Clear`.
- `SequenceCombineIndex` removes `existing[i]` using the partial element's position, not the target index, when a combined element becomes empty (*quirk*).
- Duplicate ids inside the same partial directory merge instead of failing, so id collisions inside a partial path are silent.
