# Release Checklist

Use this checklist every time a Kun Framework version is cut. The tag push triggers
`.github/workflows/release.yml` which handles build / test / AOT / pack / publish
automatically. This document covers the manual steps that surround that automation.

---

## Required GitHub Secrets

Configure these once in **GitHub → Settings → Secrets and variables → Actions**:

| Secret | Required for | Notes |
|---|---|---|
| `NUGET_API_KEY` | Stable releases pushed to nuget.org | Generate at https://www.nuget.org/account/apikeys with **Push** scope |
| `GITHUB_TOKEN` | Auto-provided by Actions | Used for pre-release push to GitHub Packages and GitHub Release creation |

Pre-releases (`-beta.N`) publish to **GitHub Packages** using the
auto-provided `GITHUB_TOKEN` — no extra secret needed.

---

## 1 — Pre-release Verification

- [ ] All CI checks green on `main` (the only long-lived branch; tags are always cut from `main`)
      - CI runs **unit tests + Testcontainers integration tests** (`Kun.IntegrationTests`) automatically
      - **Sample end-to-end tests** (`Kun.Sample.Tests`) require Aspire service-discovery env vars and must be verified locally before release; they do not run in CI
- [ ] `dotnet build -c Release /warnaserror` passes locally
- [ ] `dotnet test -c Release --filter "FullyQualifiedName!~Kun.Sample.Tests"` is fully green (unit + Testcontainers integration)
- [ ] `dotnet test samples/Kun.Sample.Tests -c Release` is fully green **locally** (sample end-to-end tests)
- [ ] AOT candidates verified locally for any module touched in this release:
  ```bash
  dotnet publish src/Kun.Core -c Release -p:PublishAot=true /warnaserror
  # repeat for each modified AOT-candidate module
  ```
- [ ] `dotnet format --verify-no-changes` passes (CI gate)
- [ ] `CHANGELOG.md` `[Unreleased]` section is up to date with all changes
- [ ] Breaking changes, if any, are documented in `CHANGELOG.md` and the PR description
- [ ] Public API XML doc comments present on all new public types/members
- [ ] If this is the first ever stable release: confirm `LICENSE` file is present in repo root (it is — MIT)

---

## 2 — Version Decision

The release workflow derives the NuGet version directly from the git tag — no manual
edit of `Directory.Build.props` is required.

Tag naming convention — **CalVer dates with NuGet-compatible suffixes**
(`vYYYY.M.D[-hotfix.N|-beta.N]`, valid date without zero-padding, always cut from `main`):

| Tag | NuGet version | Destination |
|---|---|---|
| `v2026.6.12` | `2026.6.12` | nuget.org (stable) |
| `v2026.6.12-hotfix.2` | `2026.6.12-hotfix.2` | same-day full release to nuget.org; NuGet treats the suffix as prerelease ordering, so consumers must pin this exact version and an unsuffixed stable release follows on the next working day (see §5) |
| `v2026.6.12-beta.1` | `2026.6.12-beta.1` | GitHub Packages (pre-release) |

Legacy `vX.Y.Z` tags (e.g. `v0.1.0`) are historical; the release workflow rejects
new non-CalVer tags.

The attempted numeric-suffix tag `v2026.10.12-2` was rejected by nuget.org's manifest validation. Keep the failed tag as history; do not overwrite it. `scripts/release_version.py` is now the shared preflight for the helper script and release workflow. It also checks every packed nupkg/snupkg and all Kun dependency versions before the first push. Passing the SDK parser alone is not proof of nuget.org acceptance.

`Directory.Build.props` retains `<VersionPrefix>` / `<VersionSuffix>` for local and
regular CI builds; the release workflow overrides with `-p:Version=<tag>`.

---

## 3 — Cut the Release

**Option A — helper script (recommended)**

```bash
# Ensure you are on main with a clean working tree
bash scripts/release.sh 2026.6.12
# Script updates CHANGELOG.md, commits, creates annotated tag, then prints push instructions
git push origin main
git push origin v2026.6.12
```

**Option B — manual**

```bash
# 1. Edit CHANGELOG.md: rename [Unreleased] → [2026.6.12] - YYYY-MM-DD, add fresh [Unreleased]
git add CHANGELOG.md
git commit -m "chore(release): 2026.6.12"
git tag -a v2026.6.12 -m "Kun 2026.6.12"
git push origin main
git push origin v2026.6.12
```

Once the tag is pushed the GitHub Actions workflow starts automatically.

---

## 4 — Monitor the Workflow

- [ ] Open **Actions → Release** in the GitHub UI and watch each step
- [ ] Build step — zero warnings (`/warnaserror`)
- [ ] Test step — all unit tests green
- [ ] AOT publish step — zero warnings for every candidate project
- [ ] Pack step — `.nupkg` + `.snupkg` produced in `artifacts/`
- [ ] Push step — packages appear on the target feed (nuget.org or GitHub Packages)
- [ ] GitHub Release created with auto-generated notes and `.nupkg` files attached

---

## 5 — Post-release Verification

- [ ] **NuGet.org** (stable only): search `Kun.Core` — new version visible (allow up to 15 min for indexing)
- [ ] **GitHub Packages** (pre-release): confirm package visible under **Packages** tab of the repo
- [ ] **GitHub Release**: release page has the correct tag, title (`Kun <version>`), release notes, and attached `.nupkg` / `.snupkg` artifacts
- [ ] Smoke-test install in a throwaway project:
  ```bash
  dotnet new classlib -n SmokeTest
  cd SmokeTest
  dotnet add package Kun.Core --version 2026.6.12-beta.1 --prerelease \
    --source https://nuget.pkg.github.com/idadawn/index.json
  dotnet build
  ```
- [ ] If docs site exists, verify it reflects the new version
- [ ] **Hotfix follow-up** (`-hotfix.N` releases only): cut an unsuffixed stable release on the next working day even if nothing else changed, so that the latest stable on nuget.org includes the fix; tell consumers pinned to the hotfix to move back to the unsuffixed version (first applied: `2026.10.12-hotfix.3`, followed by `2026.10.13`)

---

## 6 — Rollback Plan

### NuGet.org (stable release)

NuGet does not support deletion. Use **Unlist**:

1. Go to https://www.nuget.org/packages/Kun.Core/<version>/Delete
2. Select **Unlist** (not Delete) — the package stays downloadable by pinned versions but disappears from search
3. Repeat for every `Kun.*` package in the release

### GitHub Packages (pre-release)

Pre-releases can be fully deleted:

1. **Packages** tab → select package → **Package settings** → **Delete this version**

### Git tag

```bash
# Remove remote tag (requires repo admin rights)
git push origin --delete v2026.6.12-beta.1
# Remove local tag
git tag -d v2026.6.12-beta.1
```

Deleting the tag does not undo an already-published NuGet package — unlist first.

### Hotfix flow

If a critical bug is found post-release:

1. Prepare the fix on an isolated branch based on the affected release.
2. Fix, test, merge through a green PR, then cut `v2026.6.12-hotfix.2` from the verified commit reachable on main and delete the branch.
3. Cherry-pick the fix back to `main`
4. On the next working day, cut an unsuffixed stable release even if nothing else changed: NuGet sorts `-hotfix.N` as prerelease, so the latest stable on nuget.org would otherwise stay on the pre-fix version
