<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# skills

## Purpose

Six Kun-specific Claude Skills. They encode the framework's non-negotiable conventions so Claude-generated code lands within the rails (layering, AOT, naming, Outbox, Saga). Skills auto-load when Claude Code opens this repo; agents working in `src/Kun.*` should consult the relevant skill before generating types.

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `kun-framework-conventions/` | Total guide: layering, AOT, naming, csproj, nullable, Result, JSON, commit rules — **prerequisite for all other skills** |
| `kun-ddd-modeling/` | Aggregates, entities, value objects, domain events, specification pattern |
| `kun-cqrs-mediator/` | Commands, queries, handlers, pipeline behaviors, Mediator registration |
| `kun-outbox-events/` | Integration events, stable event IDs, event-type registry, Outbox writer, idempotent consumers, event versioning |
| `kun-saga/` | Saga definition, steps, compensation order, timeouts, context persistence |
| `kun-testing/` | Unit / Testcontainers integration / saga / gRPC contract / AOT-publish tests |

## For AI Agents

### Working In This Directory

- **Reading order**: `kun-framework-conventions` first, then the topic-specific skill(s). For cross-service flows, stack multiple skills (e.g. `conventions + cqrs-mediator + outbox-events + saga`).
- **Skills are the source of truth for code patterns**; the whitepaper is the source of truth for architectural intent. If they contradict, update both in the same PR — they must not drift.
- **Skill file**: each subdirectory contains exactly one `SKILL.md`. Frontmatter `name` and `description` are what Claude's skill router matches on — the `description` must be aggressive enough to pull Claude to the skill even when the user's prompt is vague ("帮我在 Kun 里加一个 XXX").
- **Body guideline**: < 500 lines, paired ✅/❌ code examples, concrete paths and filenames — not prose.

### Common Patterns

- Each skill ends with a "don't do" list covering the specific foot-guns for that area (e.g. `conventions` forbids `JsonSerializer.Serialize(obj)` parameterless overload; `outbox-events` forbids `AssemblyQualifiedName` in OutboxMessage).

## Dependencies

### Internal

- Skills cite `CLAUDE.md`, `CONTRIBUTING.md`, and `docs/whitepaper.md`.

<!-- MANUAL: -->
