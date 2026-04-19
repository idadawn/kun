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

- [ ] `dotnet build -c Release /warnaserror` passes locally
- [ ] `dotnet test -c Release` is fully green
- [ ] AOT-candidate modules: `dotnet publish -p:PublishAot=true /warnaserror` zero warnings
- [ ] Follows four-layer dependency rules (see `CLAUDE.md`)
- [ ] New public API has XML doc comments
- [ ] Integration events have a stable event ID registered in `IEventTypeRegistry`
- [ ] New behavior covered by unit tests; integration tests added if infra is involved
- [ ] `CHANGELOG.md` `[Unreleased]` section updated if this is a user-facing change

## Testing Notes

<!-- Describe how you verified this PR. Include terminal output or screenshots where helpful. -->

## Breaking Changes

- [ ] No breaking changes
- [ ] Contains breaking changes — migration path described below:

<!-- If breaking, describe what changes callers must make. -->
