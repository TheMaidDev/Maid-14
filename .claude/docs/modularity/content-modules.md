# Content modules (assemblies and solution entries)

## Solution and project plumbing
- The solution is `SpaceStation14.slnx` (XML). Content projects are listed explicitly; the `/Goobstation/` folder holds the six Goob projects (build type `DebugOpt` and `Tools` map to Debug there), `ModuleManager` and `PatreonParser` are mapped the same way.
- The `/RobustToolbox/` folder in the slnx is generated: `dotnet run --project RobustToolbox/Tools/Robust.SolutionGen -- update` deletes all `/RobustToolbox/` folders, re-imports the projects from `RobustToolbox/RobustToolbox.slnx` with paths prefixed `RobustToolbox/`, and honours an optional `Features` property (comma list) on a `Properties Name="RobustToolbox"` element, skipping RT projects whose `Feature` is not enabled. Rerun after every RT feature update so new RT projects appear.
- A content csproj imports, at its end: `MSBuild/Content.props` (LangVersion 14, nullable, `RA0049;RA0051` suppressed), the needed `RobustToolbox/Imports/*.props` (Shared always; Client/Server/Lidgren as needed), `Robust.Properties.targets`, and for client projects `XamlIL.targets`. Projects that define `[AutoNetworkedField]` components also import `Robust.CompNetworkGenerator.targets` (Content.Shared, Goob.Shared, Goob.Common, IntegrationTests do).
- Package versions are central in `Directory.Packages.props`; csprojs use version-less `PackageReference`. RT no longer exposes its packages transitively, so each project lists what it uses.
- Executable projects: `OutputType` is `Exe` unless `FullRelease=True`. Output goes to `bin/Content.Client` or `bin/Content.Server` (`OutputPath` in each csproj), so all module DLLs of a side land in one folder.

## The Goobstation layering (the only multi-assembly module here)
- Lowest, no Content.Shared dependency, referenced *by* Content.Shared: `Goobstation.Common` (shared), `Goobstation.Maths` (shared). `Goobstation.UIKit` (client) is referenced by Content.Client. All three reference only `Content.ModuleManager`.
- `Goobstation.Shared` references Content.Shared and is referenced by Content.Server and Content.Client.
- Top of the stack: `Goobstation.Client` references Content.Client + Goobstation.Shared; `Goobstation.Server` references Content.Server + Goobstation.Shared + Goobstation.Maths. These two have their own `Program.cs` calling `ContentStart.Start`; `runserver.sh`/`runclient.sh` run *them*, not Content.Server/Content.Client. The module that must see both the base content and the fork code therefore has to be the outermost project.
- Because Content.Server does not reference Goobstation.Server, building only `Content.Server` would produce a server without the Goob layer and `VerifyModules` (below) fails at startup.

## Discovery and start
- Server and client call `ModLoader.TryLoadModulesFrom(assemblyDir, prefix)`: all `.dll` files whose name starts with the prefix are read, sorted by their assembly references, type-checked when sandboxing is enabled (namespaces and assembly names starting with `Robust`/`Content` are whitelisted, every other referenced type must be listed in `Sandbox.yml`), then loaded. Two DLLs with the same assembly name abort loading.
- Per assembly, every type deriving `GameShared` (including `GameClient`/`GameServer`) is instantiated and receives PreInit/Init/PostInit/Update/Shutdown. The base class only documents intent (`GameClient`/`GameServer` hooks, `GameShared` for both); the side is decided by which assemblies exist in that side's output folder. A module with nothing to do still has an empty entry class (`Maths`, `UIKit`).
- Each module registers its own IoC services in its entry point (`*IoC.Register()`), and the outer modules call `IoCManager.BuildGraph()` themselves.
- After loading, the server calls `LoadCVarsFromAssembly` for each module, so `[CVarDefs]` in any module is picked up.
- A `[SkipIfSandboxed]` assembly attribute makes the loader ignore the module when sandboxing is on.

## ModuleManager and the ContentModule attribute
- `Content.ModuleManager` contains `[assembly: ContentModule(ModuleType.X)]` (`Client`, `Server`, `Shared`, `Common`). The attribute tells packaging which side an assembly whose name does not end in `.Client/.Server/.Shared` belongs to (Common, Maths = Common; UIKit = Client).
- `Content.Packaging.ModuleDiscovery` scans built `Content.*.dll` files for that attribute (reads metadata, does not load them). Client package = `Content.Client`, `Content.Shared`, `Content.Shared.Database`, `Content.ModuleManager`, everything attributed non-Server, and every `Content.*.Client`/`.Shared` directory that has a csproj. Server package = the fixed core list (`Content.Server`, `Content.Server.Database`, `Content.Shared`, ...), everything attributed non-Client, and every `.Server`/`.Shared` directory with a csproj. The attribute scan needs a prior build.
- Client builds in packaging are separate `dotnet build` calls per client project; the server is published through `Robust.Server.csproj` with `FullRelease=True`.

## ModulePack: missing-assembly guard
- `ModulePack` (`Content.Shared/Module`) is an abstract class with `PackName` and `RequiredAssemblies` (`RequiredAssembly.Client/Server/Shared(name)`). `Content.Shared`'s entry point, in PreInit, finds every subclass by reflection, instantiates it through the sandbox helper, and throws if a required assembly for the current side is not among the loaded ones. `GoobModPack` lists the six Goob assemblies. The message tells the user to clean bin and rebuild.
- Any new module can ship its own pack to get the same failure instead of silent absence.

## Tests and tools
- `Content.IntegrationTests/PoolManager.Modules.cs` discovers module assemblies by file name: `Content.` prefix plus one of the hard-coded suffixes `.Shared .Client .Server .Common .UIKit .Maths`; a name with something between prefix and suffix counts as an extra module and is loaded from the test output folder. A new suffix must be added there. The test csproj has explicit `ProjectReference`s to the Goob projects so they are built and copied.
- `Content.YAMLLinter` uses the same pool to load all modules before validating `/Prototypes`.

## What a new module project (a "Maid solution/assembly") consists of
- Folder and csproj named `Content.<Name>.<Side>` with `net10.0`, `Nullable`, the Content.props / RT imports for its side, and project references only downwards (a side module may reference Content.Shared/Client/Server; a lowest-layer module may only reference ModuleManager if Content.Shared must reference it).
- An `Entry/EntryPoint.cs` deriving the matching `GameShared`/`GameClient`/`GameServer`; an IoC registration class if it has managers.
- A `<Project>` entry in `SpaceStation14.slnx`, plus a `ProjectReference` from the executable (Goob.Server/Goob.Client today) or from the tests so it is built into `bin/Content.<Side>`.
- For a name without the `.Client/.Server/.Shared` suffix: `[assembly: ContentModule(...)]`. Optionally a `ModulePack` listing it as required.
- YAML/locale/textures for it need no registration; they sit under `Resources/`.
- Nothing in the repo is a Maid assembly today. A Maid assembly that needs Goob-layer types would sit above Goobstation.Client/Server in the reference graph and become the launched project.
