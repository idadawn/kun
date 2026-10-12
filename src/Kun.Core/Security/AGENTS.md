<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Security

## Purpose

`ICurrentUser` — user-identity abstraction derived from the validated S2S JWT. Separate from `ITenantContext` so consumers that only need tenant-scoping don't pull in user-level claims.

## Key Files

| File | Description |
|------|-------------|
| `ICurrentUser.cs` | `UserId` (nullable, from JWT `sub`), `Roles` (read-only list), `IsAuthenticated`, `PrincipalKind` |

## For AI Agents

### Working In This Directory

- **Interface only.** The concrete implementation (`JwtCurrentUser`) lives in `Kun.Security`.
- **`Roles` is a snapshot of claims at request time.** Do not cache `ICurrentUser` across requests — scoped lifetime is the contract.
- **Authorization logic does not live in `ICurrentUser`.** The interface is read-only identity data; policy evaluation (role-required, permission checks) belongs in ASP.NET Core authorization handlers or dedicated authorization pipeline behaviors.

### Common Patterns

- Pipeline behaviors that need to stamp `UpdatedBy` on audited aggregates read from `ICurrentUser.UserId`.
- `UserId` is `string` to accommodate IdP `sub` values that aren't GUIDs.

## Dependencies

### Internal

None.

<!-- MANUAL: -->
