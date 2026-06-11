#!/usr/bin/env bash
# Usage: bash scripts/release.sh <version>
#   CalVer, aligned with idadawn/iclaw since 2026-06-11 (YYYY.M.D[-N|-beta.N],
#   month/day not zero-padded, always cut from main):
#   e.g. bash scripts/release.sh 2026.6.12
#        bash scripts/release.sh 2026.6.12-2
#        bash scripts/release.sh 2026.6.12-beta.1
#
# Release pre-flight + tag preparation for Kun Framework.
#
# Phases:
#   0. CalVer shape / branch / clean-tree / tag-absence gates
#   1. dotnet build -c Release /warnaserror
#   2. dotnet test (unit tests; IntegrationTests / Sample.Tests /
#      Integration.AotTests filtered out — the maintainer runs those in
#      CI via the Build & Test + sample-aot-integration jobs)
#   3. dotnet publish -p:PublishAot=true /warnaserror for every AOT-candidate
#      module. Skipped only when clang / gcc is missing on PATH; the CI
#      aot-audit matrix is the authoritative gate in that case.
#   4. CHANGELOG.md: rename [Unreleased] → [<version>] with today's date
#   5. git commit + annotated tag (NO push)
#   6. Print explicit maintainer-review-required push instructions
#
# IMPORTANT: This script never pushes. The tag is created locally only so
# the maintainer can review (`git show v<version>`) before running
# `git push origin <branch> && git push origin v<version>` themselves.
# Never run `git push --tags` — it would push every local tag including
# experiments.
#
# Windows: run via Git Bash ("bash scripts/release.sh ...") or WSL.

set -euo pipefail

# ---------------------------------------------------------------------------
# Args
# ---------------------------------------------------------------------------
VERSION="${1:?Usage: bash scripts/release.sh <version>  (e.g. 2026.6.12 or 2026.6.12-beta.1)}"
TAG="v$VERSION"

# AOT-candidate modules — must stay in sync with .github/workflows/ci.yml
# aot-audit matrix and docs/architecture/AOT-audit-2026-04-23.md. Intentionally
# excludes AspNetCore / Dapper / Saga / EntityFrameworkCore family (non-AOT by design).
AOT_MODULES=(
  Kun.Audit
  Kun.Caching
  Kun.Configuration
  Kun.Core
  Kun.EventBus
  Kun.EventBus.RabbitMQ
  Kun.Gateway
  Kun.Grpc
  Kun.Identity
  Kun.Infrastructure
  Kun.Mediator
  Kun.MultiTenancy
  Kun.Observability
  Kun.RateLimit
  Kun.Sandbox
  Kun.Security
  Kun.Storage
  Kun.Validation
)

log()  { printf '\n\033[1;34m▶ %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m⚠ %s\033[0m\n' "$*" >&2; }
err()  { printf '\033[1;31m✗ %s\033[0m\n' "$*" >&2; }

# ---------------------------------------------------------------------------
# Phase 0: gates
# ---------------------------------------------------------------------------
log "Phase 0/5 · Gates (calver / branch / clean tree / tag-absence)"

# Validate CalVer shape: YYYY.M.D[-N|-beta.N] (aligned with iclaw, no zero-padding)
if ! echo "$VERSION" | grep -qE '^[0-9]{4}\.[0-9]{1,2}\.[0-9]{1,2}(-[0-9]+|-beta\.[0-9]+)?$'; then
  err "'$VERSION' does not look like a CalVer version (e.g. 2026.6.12, 2026.6.12-2 or 2026.6.12-beta.1)"
  exit 1
fi

BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [[ "$BRANCH" != "main" && ! "$BRANCH" =~ ^hotfix/ ]]; then
  err "releases must be cut from 'main' (or a 'hotfix/*' branch for old-version patches); current branch: '$BRANCH'"
  exit 1
fi

if ! git diff --quiet; then
  err "working tree has unstaged changes — commit or stash them first"
  exit 1
fi
if ! git diff --cached --quiet; then
  err "index has staged changes — commit or stash them first"
  exit 1
fi

if git rev-parse "$TAG" >/dev/null 2>&1; then
  err "tag '$TAG' already exists locally — delete it first with: git tag -d $TAG"
  exit 1
fi

# ---------------------------------------------------------------------------
# Phase 1: build
# ---------------------------------------------------------------------------
log "Phase 1/5 · dotnet build -c Release /warnaserror"
dotnet build -c Release /warnaserror

# ---------------------------------------------------------------------------
# Phase 2: tests
# ---------------------------------------------------------------------------
log "Phase 2/5 · dotnet test (unit only; IntegrationTests / Sample.Tests / Integration.AotTests filtered out)"
dotnet test -c Release --no-build --verbosity minimal \
  --filter "FullyQualifiedName!~Kun.IntegrationTests&FullyQualifiedName!~Kun.Sample.Tests&FullyQualifiedName!~Kun.Integration.AotTests"

# ---------------------------------------------------------------------------
# Phase 3: AOT publish sweep
# ---------------------------------------------------------------------------
log "Phase 3/5 · AOT publish sweep (${#AOT_MODULES[@]} modules)"

if ! command -v clang >/dev/null 2>&1 && ! command -v gcc >/dev/null 2>&1; then
  warn "No clang / gcc on PATH — skipping AOT publish sweep."
  warn "The CI aot-audit matrix (ubuntu-latest) is the authoritative AOT gate."
  warn "Install build-essential + clang + zlib1g-dev to run this phase locally."
else
  for mod in "${AOT_MODULES[@]}"; do
    log "    AOT publish: $mod"
    dotnet publish "src/$mod" -c Release -r linux-x64 \
      -p:PublishAot=true -p:TreatWarningsAsErrors=true \
      --verbosity minimal --nologo
  done
fi

# ---------------------------------------------------------------------------
# Phase 4: CHANGELOG rename
# ---------------------------------------------------------------------------
log "Phase 4/5 · CHANGELOG [Unreleased] → [$VERSION]"
CHANGELOG="CHANGELOG.md"
if [ ! -f "$CHANGELOG" ]; then
  err "$CHANGELOG not found in $(pwd)"
  exit 1
fi

if ! grep -q '## \[Unreleased\]' "$CHANGELOG"; then
  err "$CHANGELOG has no '## [Unreleased]' section to rename"
  exit 1
fi

DATE=$(date +%Y-%m-%d)

# Guard: if [Unreleased] has no concrete entries (only the placeholder
# "（尚无新变更。）" we seeded at Phase 1 release), abort so the user
# doesn't ship an empty changelog section.
UNRELEASED_BODY=$(awk '
  /^## \[Unreleased\]/       {flag=1; next}
  /^## \[/ && flag           {exit}
  flag {print}
' "$CHANGELOG" | grep -v '^[[:space:]]*$' | grep -v '^（尚无新变更。）$' || true)

if [ -z "$UNRELEASED_BODY" ]; then
  err "$CHANGELOG [Unreleased] section is empty — add entries before tagging"
  exit 1
fi

# Portable sed (GNU + BSD): write to .bak, delete the backup.
sed -i.bak "s|^## \[Unreleased\]|## [Unreleased]\n\n（尚无新变更。）\n\n## [$VERSION] - $DATE|" "$CHANGELOG"
rm -f "${CHANGELOG}.bak"

echo "Updated $CHANGELOG: [Unreleased] → [$VERSION] - $DATE"

# ---------------------------------------------------------------------------
# Phase 5: commit + annotated tag (NO push)
# ---------------------------------------------------------------------------
log "Phase 5/5 · git commit + annotated tag (no push)"
git add "$CHANGELOG"
git commit -m "chore(release): $VERSION"
git tag -a "$TAG" -m "Kun $VERSION"

# ---------------------------------------------------------------------------
# Done — push is the maintainer's job
# ---------------------------------------------------------------------------
cat <<EOF

============================================================================
  ✅  Tag '$TAG' created locally on branch '$BRANCH'.

  ⏸   HUMAN REVIEW REQUIRED before pushing — the script will NOT push.
      Review first:
        git show $TAG
        git log --oneline origin/$BRANCH..$BRANCH

  ⏩  When approved, the maintainer runs:
        git push origin $BRANCH
        git push origin $TAG

  ℹ   Release workflow: .github/workflows/release.yml
      Pre-release ('vYYYY.M.D-beta.N')          → GitHub Packages
      Stable ('vYYYY.M.D' / patch 'vYYYY.M.D-N') → nuget.org (needs NUGET_API_KEY)

  🛑  Never run 'git push --tags' — it would push every local tag,
      including experiments.
============================================================================
EOF
