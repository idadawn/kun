<!--
  Kun Framework — Release Notes Authoring Template

  Copy this file to `docs/releases/v<VERSION>.md` when preparing a new
  release. The release-drafter (.github/release-drafter.yml) auto-generates
  a PR-level changelog from labels; this template is the hand-authored
  "maintainer story" the release coordinator pastes into the GitHub
  Release body at tag time. Both layers coexist: drafter = PR rollup,
  docs/releases/*.md = narrative.

  Delete the `<!-- … -->` guidance blocks before publishing.

  See docs/releases/v0.1.0-phase1.md for the canonical filled-in example.
-->

# Kun Framework — v<VERSION>

_<Short one-line tagline, e.g. "First NeuroAgent-integration-ready Kun release" — YYYY-MM-DD_

---

## ✨ Highlights

<!--
  3–6 bullets surfacing what downstream consumers care most about.
  Rules of thumb:
    - Lead with the shippable capability, not the commit count.
    - One bullet per "thing the consumer will feel" — new module family,
      AOT posture change, removed friction, perf win, etc.
    - Link into docs/architecture/ADR-*.md or docs/architecture/*.md for
      the long-form rationale.
-->

- **…**

---

## 📦 Modules shipped / updated

<!--
  Table of modules this release introduces or materially changes. Skip
  modules with only transient commits (typo fixes, test tweaks). Keep the
  "Entry-point API" column terse so a consumer can paste it directly.
-->

| Capability | Module | Entry-point API |
|---|---|---|
| … | `Kun.…` | `AddKun…()` |

---

## 🔄 Breaking Changes

<!--
  List every behavior change a consumer will trip over. If genuinely none,
  write "None." — do not omit the section.

  For each breaking change, show:
    - **What** changed (symbol + old → new shape).
    - **Why** (link to ADR / issue / AOT audit / security CVE).
    - **Consumer impact** (what breaks if they do nothing?).
    - **Migration** (shortest patch to restore green).
-->

- None.

_or_

- **`Kun.Foo.Bar` rename**: `OldMethod` → `NewMethod`.
  - **Why**: ADR-NNN — rationale.
  - **Impact**: Callers of `OldMethod` will fail to compile.
  - **Migration**: global replace or adapter class.

---

## 🗺️ Migration Guide

<!--
  Optional. Include only when consumers need a sequenced upgrade (version
  pinning + DI wiring changes + csproj edits). For pure-feature releases
  without Breaking Changes, delete this section.
-->

1. **Pin package versions**:
   ```xml
   <PackageVersion Include="Kun.Core" Version="<VERSION>" />
   ```
2. …

---

## 🚧 Known Limitations

<!--
  Ship-blockers turned "accepted limitations". Be honest — do not hide
  placeholders. Each bullet points at a follow-up tracker (e.g.
  `P2-KN-Saga-EF-Split`) where possible.
-->

- …

---

## 🧪 Known-Good 组合

<!--
  The exact component versions this release was validated against.
  Includes SDK pin, TargetFramework, .NET Aspire, EF Core, broker clients,
  OTel, OS / Docker prerequisites.
-->

| 组件 | 版本 | 备注 |
|---|---|---|
| .NET SDK | `10.0.100+` | `global.json` rollForward: latestFeature |
| `TargetFramework` | `net10.0` | |
| .NET Aspire | `9.0.0` | |
| … | | |

---

## 🔗 相关文档

<!--
  Every release notes should end with a curated link list so a reader can
  drill into:
    - CHANGELOG section for the granular SHAs.
    - whitepaper chapter if any.
    - module-map if modules were added/updated.
    - ADR(s) that landed in this release.
    - AOT audit if status changed.
-->

- [`CHANGELOG.md` §`[<VERSION>]`](../../CHANGELOG.md)
- [Module map](../architecture/module-map.md)
- ADRs: …

---

## 🙏 Credits

<!--
  Team attribution — who drove this release across tmux windows / pair
  partnerships / issue reviewers. Keep factual, no emoji spam.
-->

…
