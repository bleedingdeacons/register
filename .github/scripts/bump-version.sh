#!/usr/bin/env bash
#
# Computes the next version for a pull request from the commits on it, and
# writes it into the Register csproj.
#
# Rules (see .github/workflows/version.yml):
#   any `feat:` commit  -> minor bump, patch reset to 0
#   anything else       -> patch bump
#   never major         -> a major release is a deliberate act, taken by hand
#
# ApplicationVersion — the Android versionCode — advances by one alongside it.
# It is what decides whether a build can update an existing install, so it must
# never go backwards or stall.
#
# The baseline is deliberately read from the BASE branch rather than from the
# working tree. That is what makes this idempotent: re-running against an
# already-bumped branch computes the same target, sees it is already there, and
# does nothing. Without it, every push would bump again and the version would
# climb once per commit.
#
# Usage:  bump-version.sh [base-ref]        (default: origin/main)
# Local dry run:  .github/scripts/bump-version.sh origin/main
#
set -euo pipefail

CSPROJ="TheBleedingDeacons.Intergroup.Register/TheBleedingDeacons.Intergroup.Register.csproj"
BASE_REF="${1:-origin/main}"

read_prop() { # read_prop <text> <property>
    printf '%s' "$1" | sed -n "s|.*<$2>\([^<]*\)</$2>.*|\1|p" | head -1
}

emit() { # emit <key> <value> — GitHub output when in CI, otherwise just echo
    echo "$1=$2"
    [ -n "${GITHUB_OUTPUT:-}" ] && echo "$1=$2" >> "$GITHUB_OUTPUT"
    return 0
}

base_csproj="$(git show "$BASE_REF:$CSPROJ")"
base_version="$(read_prop "$base_csproj" ApplicationDisplayVersion)"
base_code="$(read_prop "$base_csproj" ApplicationVersion)"

# Three parts, always. AssemblyVersion is $(ApplicationDisplayVersion).0, so a
# fourth component here produces a five-part version and fails the build.
if ! [[ "$base_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "::error::ApplicationDisplayVersion on $BASE_REF is '$base_version'; expected three numeric parts." >&2
    exit 1
fi
if ! [[ "$base_code" =~ ^[0-9]+$ ]]; then
    echo "::error::ApplicationVersion on $BASE_REF is '$base_code'; expected an integer." >&2
    exit 1
fi

# Subjects only. Scanning bodies too would let prose like "reverts the feat:
# commit" trigger a minor bump.
subjects="$(git log --no-merges --format='%s' "$BASE_REF..HEAD")"

if [ -z "$subjects" ]; then
    echo "No commits on top of $BASE_REF; nothing to version."
    emit changed false
    exit 0
fi

if printf '%s\n' "$subjects" | grep -qiE '^feat(\([^)]*\))?!?:'; then
    bump=minor
else
    bump=patch
fi

IFS=. read -r major minor patch <<< "$base_version"
case "$bump" in
    minor) minor=$((minor + 1)); patch=0 ;;
    patch) patch=$((patch + 1)) ;;
esac
new_version="$major.$minor.$patch"
new_code=$((base_code + 1))

current_version="$(read_prop "$(cat "$CSPROJ")" ApplicationDisplayVersion)"
current_code="$(read_prop "$(cat "$CSPROJ")" ApplicationVersion)"

echo "base $BASE_REF: $base_version (code $base_code)"
echo "bump:          $bump"
echo "target:        $new_version (code $new_code)"
echo "working tree:  $current_version (code $current_code)"

if [ "$current_version" = "$new_version" ] && [ "$current_code" = "$new_code" ]; then
    echo "Already at the target version; nothing to do."
    emit changed false
    emit version "$new_version"
    exit 0
fi

sed -i "s|<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>|<ApplicationDisplayVersion>$new_version</ApplicationDisplayVersion>|" "$CSPROJ"
sed -i "s|<ApplicationVersion>[^<]*</ApplicationVersion>|<ApplicationVersion>$new_code</ApplicationVersion>|" "$CSPROJ"

emit changed true
emit version "$new_version"
emit code "$new_code"
emit bump "$bump"
