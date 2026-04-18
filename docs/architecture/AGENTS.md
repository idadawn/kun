<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# architecture

## Purpose

Architecture diagrams and module-level reference material. The module map shows dependency edges between Kun modules and the canonical request paths (command → DB → Outbox → consumer; query → DB read; Saga step progression).

## Key Files

| File | Description |
|------|-------------|
| `module-map.md` | Dependency graph (mermaid), layer bands, and three annotated call paths: HTTP → DB → event, query, Saga |

## For AI Agents

### Working In This Directory

- **Keep the module map in sync with reality.** If a module's dependencies change, update the mermaid graph here *and* the matching section of `../whitepaper.md` — the whitepaper copies the graph inline.
- Diagrams stay in mermaid so they render on GitHub and can be diffed in text. Do not replace with binary images.
- Three canonical paths (HTTP-write, HTTP-read, Saga) are the examples readers learn from — if the framework adds a fourth (e.g. websocket / SignalR), add a fourth path here rather than overloading an existing one.

## Dependencies

### Internal

- Referenced from `README.md` (`📖 架构设计白皮书` section) and from `../whitepaper.md`.

<!-- MANUAL: -->
