<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# docs

## Purpose

Architecture documentation for Kun Framework. The whitepaper is the canonical long-form design rationale; the architecture subfolder contains diagrams and module maps that are referenced from the whitepaper and README.

## Key Files

| File | Description |
|------|-------------|
| `whitepaper.md` | Full design whitepaper: vision, tech-stack rationale, module breakdown, code samples, DDD/CQRS/Outbox/Saga/S2S-JWT detailed specs, roadmap |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `architecture/` | Module map and dependency diagrams (see `architecture/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- The whitepaper is the **ground truth** for architectural intent. When `.claude/skills/` and the whitepaper diverge, the whitepaper wins for design questions, but the skills win for concrete code conventions (naming, AOT, layering).
- **Do not edit docs to match code drift** — if code violates the whitepaper, fix the code. If the design has genuinely evolved, update both the whitepaper and the affected skill.
- Keep diagrams in mermaid inside markdown so GitHub renders them natively; avoid binary images unless the diagram can't be expressed in mermaid.
- Chinese and English are both used in docs — keep the existing language of a document when editing.

### Common Patterns

- Section headings use emoji prefixes in the README/whitepaper style; match the surrounding file's tone.
- External-facing docs (README, CONTRIBUTING) are Chinese-first; architectural internals are Chinese-first with English code identifiers.

## Dependencies

### Internal

- `README.md` and `CONTRIBUTING.md` at the repo root link to this folder.

<!-- MANUAL: -->
