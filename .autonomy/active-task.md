---
id: KN-SELF-005
title: IAuthenticatedUser vs ICurrentUser — guidance doc + XML cross-links
status: done
planner_commit: 7bddaeb
executor_head: d5d9b68
estimated_commits: 2
actual_commits: 2
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-005 — `IAuthenticatedUser` vs `ICurrentUser` naming-consistency sweep

## Why

ADR-010 introduced `IAuthenticatedUser` (cookie / browser flow) as
the browser-facing extension of `ICurrentUser` (S2S JWT flow). The
inheritance is clean (`IAuthenticatedUser : ICurrentUser`) and the
DI registration in `Kun.Identity.AspNetCore` binds both to the same
request-scoped instance — so callers choose by the fields they need,
not by "which one is right here". Without durable guidance, the next
sprint could easily invent `ICurrentUserEx` / `IEnhancedCurrentUser` /
a per-service adapter — exactly the drift ADR-010 was written to
end.

The BACKLOG lists three acceptable deliverables (editorconfig rule,
hand-rolled reflection-at-test-time rule, guidance doc). A guidance
doc is the cheapest and highest-signal option — the other two need a
"maintainer who remembers to update the allow-list when a legit new
subinterface lands", which is the same shape of drift we're trying
to avoid.

## Specs to read first

1. `kun/CLAUDE.md` — §Specs-first
2. `kun/docs/architecture/ADR-008-Kun.Identity-module.md` — parent
   context for the `ICurrentUser` surface (S2S JWT side)
3. `kun/docs/architecture/ADR-010-Kun.Identity-cookie-session.md` —
   rationale for adding `IAuthenticatedUser` and the exact drift
   language ADR-010 warned about
4. `kun/src/Kun.Core/Security/ICurrentUser.cs` — 3 members
5. `kun/src/Kun.Identity/Abstractions/IAuthenticatedUser.cs` —
   extends `ICurrentUser`, adds `TenantId` / `Email` / `Name` /
   `Principal`
6. `kun/src/Kun.Core/Security/AnonymousCurrentUser.cs` + existing
   `JwtCurrentUser.cs` / `KunCurrentUser.cs` implementations — the
   two bindings that exist today

## Acceptance criteria

- **AC-1**: New guidance doc
  `docs/architecture/when-to-use-currentuser.md`. Sections:
  - Two interfaces, one instance (current state).
  - Decision table — which interface for which consumer scenario
    (S2S gRPC handler, browser endpoint, background worker, unit
    test).
  - How to extend correctly: add fields to `IAuthenticatedUser` for
    browser-only concerns, add fields to `ICurrentUser` only when
    the S2S path *also* needs them, **never** ship an
    `I*CurrentUser`-shaped third sibling.
  - Quick "anti-patterns" section listing specific forbidden
    names / shapes so reviewers can cite the doc.
- **AC-2**: XML doc on `ICurrentUser.cs` and `IAuthenticatedUser.cs`
  gains a `<seealso>` pointer to the guidance doc so IDEs surface it
  at the code site.

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(identity): guidance on ICurrentUser vs IAuthenticatedUser` | AC-1 | new file, self-contained |
| 2 | `docs(identity): cross-link current-user interfaces to guidance doc` | AC-2 | XML comment edits on both interfaces |

## AOT / build gates

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true`
      still 0/0 (XML comment edits must produce valid crefs)
- [ ] No code changes — AOT / tests unaffected

## Out of scope

- Reflection-at-test-time guard that asserts no new `ICurrentUser`
  subinterface lands outside the known set. Added value is low for
  the cost — the guidance doc + XML pointer is the primary lever.
  Tracked as potential future tightening once a concrete drift
  incident occurs.
- editorconfig / `.globalconfig` analyzer rule — same rationale.

## Progress log

- 2026-04-24 349da89 KN-SELF-004 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-005 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-005 done (<SHA>) — current-user guidance doc + XML cross-links." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-005_done.txt`
4. Planner next pick: BACKLOG is now empty of eligible (S/M-sized)
   items — KN-SELF-006 is L-sized Saga EF split (parked). Re-scan
   the codebase for a fresh P1 or P2 item to add to BACKLOG before
   picking, OR add a brand-new KN-SELF-xxx item addressing a
   concrete pain point surfaced during the last 4 tasks.
