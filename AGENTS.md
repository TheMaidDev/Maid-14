# Maid-14 Agent Instructions

## Context & References
- Fork of SS14 Goob Station ("upstream").
- Target SDK: `.NET 10` (RT v294.0.1; solution is `SpaceStation14.slnx`, update it with `dotnet run --project RobustToolbox/Tools/Robust.SolutionGen -- update` after every RT feature update).
- Engine submodule: `RobustToolbox/` (never modify).
- Old reference build: `/home/ebi-claude/ss14-wd-022024` (commit `87a3ddb4ae`, read-only reference).

## Repository Rules & Boundaries
- All GitHub PRs are tracked locally under `origin/pr/<number>`.
- Isolate session work and PR reviews in worktrees under `/home/ebi-claude`.
- You probably have access to push into the PR branch. If not, request rights from user with @CREAsTIVE ping.
- Never push to main directly without explicit permission.
- Never edit `Content.Goobstation.*` or `Content.*.Database` without explicit permission.
- Do not run `Content.Tests`, `Content.IntegrationTests`, or full builds unless asked.

## Upstream Modification Conventions
- Everything in `_Maid/` folders is Maid-owned; edit freely without markers.
- Outside `_Maid/`, keep upstream modifications minimal and marked:
  - Small/inline: `// MAID {change}`
  - Block replacements: wrap with `// MAID BEGIN {change}` and `// MAID END {change}`, preserving old code commented out inside `/* ... */`.
  - YAML: use `#` comments per line.
  - Localization: no comments in `.ftl` files.
  - `{change}`: short, consistent 2–5 word task label.

## Documentation
- Project knowledge base for agents lives in `.claude/docs/`; start with `.claude/docs/ARCHITECTURE.md`.
- `.claude/docs/ecs.md` is the ECS overview (entities, components, systems, with details in `.claude/docs/ecs/`); `.claude/docs/systems.md` is the entry point for content systems (per-system docs go in `.claude/docs/systems/<system>.md`); `.claude/docs/rsi.md` covers sprites/RSI/Appearance.
- Read the relevant docs before studying code from scratch.
- When writing, updating or auditing these docs, use the `doc-writer` skill (`.claude/skills/doc-writer`).
