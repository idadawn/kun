#!/usr/bin/env bash
# Usage: bash scripts/release.sh <version>
#   e.g. bash scripts/release.sh 1.0.0-rc.1
#        bash scripts/release.sh 1.0.0
#
# What it does:
#   1. Validates branch (must be main or develop) and working tree (must be clean).
#   2. Updates CHANGELOG.md: renames [Unreleased] to [<version>] with today's date
#      and inserts a fresh [Unreleased] section above it.
#   3. Commits CHANGELOG.md and creates an annotated git tag.
#   4. Prints the push commands — it does NOT push automatically.
#
# Windows: run via Git Bash ("bash scripts/release.sh ...") or WSL.

set -euo pipefail

# ---------------------------------------------------------------------------
# Args
# ---------------------------------------------------------------------------
VERSION="${1:?Usage: bash scripts/release.sh <version>  (e.g. 1.0.0-rc.1)}"
TAG="v$VERSION"

# ---------------------------------------------------------------------------
# Validate semver shape (loose check — major.minor.patch[-prerelease])
# ---------------------------------------------------------------------------
if ! echo "$VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$'; then
  echo "ERROR: '$VERSION' does not look like a SemVer version (e.g. 1.0.0 or 1.0.0-rc.1)"
  exit 1
fi

# ---------------------------------------------------------------------------
# Branch check
# ---------------------------------------------------------------------------
BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [[ "$BRANCH" != "main" && "$BRANCH" != "develop" ]]; then
  echo "ERROR: releases must be cut from 'main' or 'develop' (current branch: '$BRANCH')"
  exit 1
fi

# ---------------------------------------------------------------------------
# Clean working tree check
# ---------------------------------------------------------------------------
if ! git diff --quiet; then
  echo "ERROR: working tree has unstaged changes — commit or stash them first"
  exit 1
fi
if ! git diff --cached --quiet; then
  echo "ERROR: index has staged changes — commit or stash them first"
  exit 1
fi

# ---------------------------------------------------------------------------
# Tag must not already exist
# ---------------------------------------------------------------------------
if git rev-parse "$TAG" >/dev/null 2>&1; then
  echo "ERROR: tag '$TAG' already exists locally — delete it first with: git tag -d $TAG"
  exit 1
fi

# ---------------------------------------------------------------------------
# CHANGELOG.md update
# ---------------------------------------------------------------------------
CHANGELOG="CHANGELOG.md"
if [ ! -f "$CHANGELOG" ]; then
  echo "ERROR: $CHANGELOG not found in $(pwd)"
  exit 1
fi

if ! grep -q '## \[Unreleased\]' "$CHANGELOG"; then
  echo "ERROR: $CHANGELOG has no '## [Unreleased]' section to rename"
  exit 1
fi

DATE=$(date +%Y-%m-%d)

# Portable sed: use .bak temp file, then remove it (works on GNU sed and BSD sed)
sed -i.bak "s|^## \[Unreleased\]|## [Unreleased]\n\n## [$VERSION] - $DATE|" "$CHANGELOG"
rm -f "${CHANGELOG}.bak"

echo "Updated $CHANGELOG: [Unreleased] -> [$VERSION] - $DATE"

# ---------------------------------------------------------------------------
# Commit and tag
# ---------------------------------------------------------------------------
git add "$CHANGELOG"
git commit -m "chore(release): $VERSION"
git tag -a "$TAG" -m "Kun $VERSION"

# ---------------------------------------------------------------------------
# Done — print push instructions
# ---------------------------------------------------------------------------
echo
echo "---------------------------------------------------------------------"
echo "  Tag '$TAG' created on branch '$BRANCH'."
echo "  Push to trigger the release workflow:"
echo
echo "    git push origin $BRANCH"
echo "    git push origin $TAG"
echo
echo "  Release workflow: .github/workflows/release.yml"
echo "  Pre-release ($TAG)?  -> publishes to GitHub Packages"
echo "  Stable release?      -> publishes to nuget.org (requires NUGET_API_KEY secret)"
echo "---------------------------------------------------------------------"
