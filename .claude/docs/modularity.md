# Modularity

How Maid-14 stacks layers (engine, upstream content, fork layers, Maid) and which mechanisms let a layer add or alter things without editing the layer below. Two mechanisms are large enough for their own files:
- [modularity/content-modules.md](modularity/content-modules.md): Content assemblies as separate projects/solution entries, how the engine finds and starts them, packaging, tests, the ModulePack check, and what a new module project needs.
- [modularity/partial-prototypes.md](modularity/partial-prototypes.md): merging YAML into already-defined prototypes (`!Remove`, `!Clear`, `!Index`, `!PartialOnly`), the abstract-file list, variants, and the fact that partials are not wired up in this repo yet.

## The layers
- **RobustToolbox** (submodule, never edited): `Robust.Shared`, `Robust.Server`, `Robust.Client`. Games depend on it only through `RobustToolbox/Imports/*.props` (`Shared.props`, `Client.props`, `Server.props`, `Lidgren.props`, ...); direct project references to RT are not allowed since RT 269.
- **Upstream content**: `Content.Shared/Server/Client` plus `Content.Shared.Database`/`Content.Server.Database`. Also contains many fork code folders merged in by Goob (`_DV`, `_Imp`, `_NF`, `_RMC14`, `_Shitmed`, `_Starlight`, `_White`, ...). These are folders inside the core projects, not separate assemblies.
- **Goobstation layer**: six separate assemblies (`Content.Goobstation.*`), the only real multi-assembly module in the repo. Details in the content-modules doc.
- **Maid**: no assembly of its own. Maid code is the `_Maid` folder inside each core project (`Content.Shared/_Maid`, `Content.Server/_Maid`, `Content.Client/_Maid`), plus `Resources/Prototypes/_Maid`, `Resources/Textures/_Maid`, `Resources/Locale/<lang>/_Maid`, `Resources/_Maid` (data files such as chat filters). Edits to upstream files outside those folders are marked with `// MAID ...` comments (see root instructions).

## Mechanisms by what they modularise
| Need | Mechanism | Notes |
|---|---|---|
| New code in its own DLL | Content module project (`Content.<Name>.{Shared,Client,Server}`) | auto-loaded from the output folder by name prefix `Content.`; see content-modules doc |
| Fork-owned files | `_<Fork>` folders | convention only; the engine treats every file in `Resources/Prototypes` and `Resources/Locale/<lang>` recursively, so folder names carry no semantics |
| Add a prototype | new YAML file, unique id | duplicate id across files is a load error unless the directory was registered as partial |
| Change one field of an existing prototype | partial prototype (not enabled) or `parent:` + new id, or an edit with a `# MAID` marker | see partial-prototypes doc |
| Remove an upstream prototype without deleting files | `Resources/IgnoredPrototypes/*.yml` | marks prototypes abstract; inheritance survives, usages must be removed |
| Many near-identical prototypes | `id: !type:CreateVariants` + `values` | engine feature, expands one YAML entry into several prototypes |
| Config variables | `[CVarDefs]` class in any module | the server loads CVars from every loaded module assembly; Maid's live in `Content.Shared/_Maid/CVars` |
| Registered services | `IoCManager.Register` in a module's own entry point | each module has an `Entry/EntryPoint.cs` and a `*IoC` class |
| Client-only components named in shared YAML | `IgnoredComponents.List` (server) / `IgnoreMissingComponents` (client) | see ARCHITECTURE section 2 |

## Load order facts that modularity relies on
- The mod loader loads every `Content.*.dll` from the assembly folder (prefix from `manifest.yml` `assemblyPrefix`, default `Content.`) in topological order of assembly references, then every `GameShared`-derived type in each assembly is instantiated as an entry point.
- Entry-point callbacks run per phase for all modules before the next phase: PreInit, then Init, then PostInit. Content `Init` runs before `IPrototypeManager.Initialize` on both server and client, which is why prototype-loading switches (abstract/partial lists) are registered in `Init`.
- Localisation and prototypes are read from the virtual `/Locale` and `/Prototypes` roots; all `.yml` files below, except names starting with `.`, are loaded.
