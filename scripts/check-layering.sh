#!/usr/bin/env bash
# Layering check for Kun Framework.
#
# Asserts `Kun.Core` has no third-party PackageReference entries; the only
# allowed dependencies are `Microsoft.Extensions.*` abstraction packages.
# This guards the framework's "Core = BCL only" rule (see CLAUDE.md and
# `.claude/skills/kun-framework-conventions/SKILL.md`).
#
# Exit codes:
#   0 - clean
#   1 - layering violation detected
#   2 - script / repo-state error
#
# Run from any directory; paths resolve relative to the repo root.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CORE_PROJ="$REPO_ROOT/src/Kun.Core/Kun.Core.csproj"

if [[ ! -f "$CORE_PROJ" ]]; then
    echo "error: Kun.Core csproj not found at $CORE_PROJ" >&2
    exit 2
fi

# Extract Include="..." values from every <PackageReference ...> line.
# `|| true` because grep returns 1 when there are no matches (which is fine).
refs="$(grep -E '<PackageReference[^>]+Include=' "$CORE_PROJ" \
        | sed -E 's/.*Include="([^"]+)".*/\1/' \
        || true)"

total=0
violations=()

while IFS= read -r ref; do
    [[ -z "$ref" ]] && continue
    total=$((total + 1))
    case "$ref" in
        Microsoft.Extensions.*) ;;  # abstraction packages are allowed
        *) violations+=("$ref") ;;
    esac
done <<< "$refs"

if (( ${#violations[@]} > 0 )); then
    echo "FAIL: Kun.Core layering violation — third-party PackageReference(s) detected." >&2
    echo "  File: $CORE_PROJ" >&2
    for v in "${violations[@]}"; do
        echo "    - $v" >&2
    done
    echo >&2
    echo "  Kun.Core must stay BCL-only (with Microsoft.Extensions.* abstractions as the only exception)." >&2
    echo "  Move this dependency into Kun.Infrastructure, Kun.Mediator, or a higher layer." >&2
    exit 1
fi

echo "OK: Kun.Core layering clean (${total} package ref(s) checked; all BCL/abstraction)."
