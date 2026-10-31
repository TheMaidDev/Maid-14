# RT update v266.0.0 -> v293.0.1 TODO

Branch: rt-update-293-0-1 (based on agents-docs). Markers: `// MAID rt-update`.

- [x] Read RT release notes 267..293
- [x] RobustToolbox -> v293.0.1, global.json -> .NET 10 SDK
- [x] .sln -> .slnx + SolutionGen, Imports/*.props, net10.0, C# 14, package bumps
- [x] Mechanical compile fixes (Shared, Server, Client, Goobstation.*, tools, tests)
- [x] YAML linter clean (Box2/Box2i, Thickness, Timer, icons, occluder, loc)
- [x] Content.Tests green; selected integration tests green
- [x] CONTROVERSIAL.md written
- [ ] CI workflows: apply RT_UPDATE/workflows-dotnet10.patch (needs `workflow` scope)
- [ ] Full integration test suite (needs more RAM / CI)
- [ ] Real boot smoke test with a player, DB smoke test (Postgres + SQLite), UI visual pass
- [ ] Marker cleanup: drop `// MAID rt-update` where code matches Goob upstream
