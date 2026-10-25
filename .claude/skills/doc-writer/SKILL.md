---
name: doc-writer
description: Create, update, verify and prune agent-oriented documentation of a codebase (architecture, modules, mechanisms). Use when documenting a project or module, updating docs after contract/architecture changes, or auditing docs for rot and bloat. Not for inline comments, docstrings or commit messages.
---

# Agent documentation (doc-writer)

Docs are a reference for a future agent with no conversation history. They contain facts learned by reading the code, so the same code need not be re-studied. Where the docs live and any project-specific layout are defined by the project (its entry/architecture document, root instructions); follow those. If no docs folder exists, ask the user where to create it and do not commit it without permission.

## Content
**In:**
- Facts that save searching: what APIs/attributes exist and do, how identifiers are obtained, lifecycle and event ordering, networking/persistence behaviour, naming conventions the code relies on.
- Architecture shape: what owns what, how parts relate, where data flows, plus invariants and failure modes of a mechanism.
- Stable foundations (third-party frameworks, engines, libraries): name concrete symbols (types, fields, methods, attributes) and explain them at the level of logic.
- Application code, including customized forks of upstream projects: name only the key entry points, components and events; spend the words on flow and relationships, so routine refactors do not rot the docs.
- Tunable values (numbers, defaults, timings, ranges) go stale fastest: name the config or constant that holds them and say what it controls; quote a value only when behaviour depends on the exact number.

**Out:**
- Introductions, filler, tutorials, obvious or generic knowledge.
- Copied code and file+line references. Describe what the code does; refer to it by symbol name and by area/folder. A single short identifier or expression inline is fine.
- Project rules, requirements, process instructions (they belong in the project's own instruction files).
- "How to add X" recipes; describe how the mechanism works instead.
- ASCII diagrams. Use text; a mermaid diagram only if text truly cannot do it.
- Unverified claims (see Verification).

**Fact vs. filler.** Good: "`save()` is asynchronous; read the file only after `flush()` completes." Bad: "Saving is handled by the persistence layer." A sentence belongs in the docs if a reader could act wrongly without it.

## Structure
- One document per concept (single source of truth). No versioned clones, scratch or summary files.
- An entry document lists the other docs and where to read first; keep it short.
- Organize by domain/feature, not by mirroring the folder tree or framework layers (no docs for utils/helpers/dtos). A layout similar to the sources is fine when it matches the domain.
- Hierarchy: `X.md` is the overview of folder `X/`. It can be a full description of system X (core concepts, data, flow, anything the parts share); self-contained or large sub-aspects move to children (e.g. `payments.md` explains the payment flow and shared invariants, `payments/refunds.md` covers refunds). Children may nest to any depth. Split by this test: shared understanding stays in the parent, detail needed only when working on one sub-aspect goes to a child.
- A hub never merely lists its children. Every link carries a short note on what the child covers and how it fits the parent. A parent that is mostly links is fine, but it still says what the area is and how it works at a glance. Do not restate what a directory listing already shows.
- Create a topic file only after studying that topic in depth. A hub may name an adjacent area it did not study only as "not documented", without describing it.
- Tie-breaker for the hub/child split: if a reader needs the detail to follow the parent's own flow, it stays in the parent.
- Size budget: about 100 lines of normal width (roughly 6 KB) per document; packing text into very long lines does not satisfy it. If it grows past that, compress or split along existing boundaries. Do not duplicate content across files; link.
- Consistent file naming and a single language across the docs.
- Sections are optional: include only what has verified content (typical ones are scope, how it works, integration points, invariants and failure modes). Never fill a section for the sake of the structure.

## Verification
A claim counts as verified only if you read the code that implements it, not just a name, a comment, or a caller. This includes statements about why something works or how parts fit together: if you saw only one side of the connection (a call but not its reader), omit it. Check that every symbol you name exists. If you could not verify something, omit it; do not hedge it in the text.

## Workflow
1. Inventory: read the existing docs and the entry document first.
2. Read the code. Do not write from memory of how similar projects behave; customized or forked code differs.
3. Write facts, then re-check each claim against the code per Verification.
4. Apply surgically: edit existing statements in place, delete obsolete ones, never append historical notes.
5. When files are added, moved or removed, update the parent hub's notes and the entry document.
6. Update docs when contracts or architecture change, or when asked.
