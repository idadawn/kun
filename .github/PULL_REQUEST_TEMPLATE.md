# Pull Request

## Summary

<!-- Briefly explain the purpose and main changes of this PR. -->

## Change Type

<!-- Check all that apply. The PR title MUST follow Conventional Commits so the
     release-drafter can auto-categorize and auto-label this PR. -->

- [ ] `feat`: new feature
- [ ] `fix`: bug fix
- [ ] `perf`: performance improvement
- [ ] `refactor`: code restructuring (no behavior change)
- [ ] `docs`: documentation only
- [ ] `test`: test additions or corrections
- [ ] `build` / `ci`: build system or workflow changes
- [ ] `chore`: maintenance, dependencies, tooling

**PR title format**: `type(scope): short description`
**Breaking change**: append `!` after type/scope — e.g. `feat(saga)!: rename ISagaStep`

## Related Issues

Closes #

## Self-Review Checklist

### Build & test

- [ ] `dotnet build -c Release /warnaserror` passes locally
- [ ] `dotnet test -c Release` is fully green
- [ ] New behavior covered by unit tests; integration tests added if infra (EF / RabbitMQ / Redis) is involved

### Framework conventions

- [ ] Follows four-layer dependency rules (Core → Abstraction → Infrastructure → Host); see `CLAUDE.md`
- [ ] `Kun.Core` still has no third-party `PackageReference` (`scripts/check-layering.sh` passes)
- [ ] New public API has XML doc comments; `PublicAPI.Unshipped.txt` updated for any shipped module

### AOT hygiene

- [ ] AOT-candidate modules: `dotnet publish -p:PublishAot=true /warnaserror` zero warnings
- [ ] New module: `<IsAotCandidate>` is declared explicitly (true or false); `.github/workflows/ci.yml` `aot-audit` matrix updated if needed
- [ ] Flipping a module's AOT status: `docs/architecture/AOT-audit-2026-04-23.md` and `docs/architecture/module-map.md` are both updated

### Events & sagas

- [ ] Integration events have a stable event ID registered in `IEventTypeRegistry` (format `{context}.{name}.v{version}`)
- [ ] New sagas: compensation order documented + timeout value justified

### Docs

- [ ] `CHANGELOG.md` `[Unreleased]` section updated if this is a user-facing change
- [ ] Adding / renaming a module: `docs/architecture/module-map.md` Phase 1 matrix + top-level `README.md` 📦 模块矩阵 refreshed
- [ ] Cross-cutting decision: a new `docs/architecture/ADR-*.md` exists (and is linked from `docs/whitepaper.md` §13 for Phase 1-era ADRs)

## Testing Notes

<!-- Describe how you verified this PR. Include terminal output or screenshots where helpful. -->

## Breaking Changes

- [ ] No breaking changes
- [ ] Contains breaking changes — migration path described below:

<!-- If breaking, describe what changes callers must make. -->
