<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# .claude

## Purpose

Kun-specific Claude Skills that auto-load when Claude Code opens this repo. Six skills encode the framework's non-negotiable conventions so Claude-generated code complies with layering, AOT, naming, Outbox, and Saga rules without re-explaining them every turn.

## Key Files

| File | Description |
|------|-------------|
| `README.md` | Skill index, trigger conditions, and prerequisite graph |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `skills/` | The six `kun-*` skill packages (see `skills/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- **Do not create new skills ad hoc**. Adding or modifying a skill is an architectural decision — it changes how every future task is executed. Confirm scope with the user.
- When editing a skill:
  - Keep the `description:` frontmatter aggressive (it's the primary trigger) — see `kun-framework-conventions` for the pattern.
  - Keep skill bodies under ~500 lines.
  - Prefer paired ✅/❌ code examples over prose.
  - When the framework's core design evolves, the affected skill **must** be updated in the same PR.
- **`kun-framework-conventions` is the prerequisite** for all other skills. If you change a rule there, audit the other five skills for consistency.

### Common Patterns

Skill directory layout: `.claude/skills/{skill-name}/SKILL.md`. Each skill's YAML frontmatter:

```yaml
---
name: {skill-name}
description: {one-line trigger condition — must pull Claude to this skill}
---
```

## Dependencies

### Internal

- Skills cite and reinforce rules in `CLAUDE.md`, `CONTRIBUTING.md`, and `docs/whitepaper.md`. Keep them synchronized.

<!-- MANUAL: -->
