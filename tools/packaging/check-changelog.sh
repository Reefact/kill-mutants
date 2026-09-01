#!/bin/sh
# Refuses a release whose train has no changelog entry for the version being published.
#
#     tools/packaging/check-changelog.sh <train> <version> [root]
#
# Exits 0 when every changelog belonging to the train carries a dated heading for the
# version, non-zero otherwise.
#
# ── Why this exists ───────────────────────────────────────────────────────────
# Nothing else in a release pipeline reads a changelog, so nothing else can notice that
# the version being published is documented nowhere — or is still filed under
# "Unreleased" while the tag says it shipped. Such a release is green in every other
# respect, which is precisely the problem: the omission is caught only when somebody
# looks, and looking is not a check. This was learned the expensive way in a sibling
# repository, where fourteen packages went out at 1.0.0 against changelogs saying they
# had not.
#
# The gap it closes is narrow and worth naming: this proves an entry EXISTS and is
# dated. It cannot prove the entry is true. A heading over prose describing another
# release passes here, and only a reader catches that.
#
# ── Which file belongs to a train ─────────────────────────────────────────────
# The projects on the train are the ones declaring it (trains.sh, projects_of), so
# membership is discovered rather than listed here, for the reason it is discovered
# there. A train whose projects each keep a CHANGELOG.md beside them is checked against
# those; a train whose projects carry none falls back to the repository root's
# CHANGELOG.md. The fallback is expressed as a shape rather than named by train id, so a
# second train sharing that shape does not have to be remembered.
#
# ── Where it runs ─────────────────────────────────────────────────────────────
# release.yml, before anything is packed, and only when the run actually publishes. A
# dry run passes a throwaway version (0.0.0-dryrun) that no changelog documents and no
# changelog should: rehearsing the pipeline is not a claim that a version shipped.
set -eu

if [ $# -lt 2 ] || [ $# -gt 3 ]; then
  printf 'usage: %s <train> <version> [root]\n' "$0" >&2
  exit 2
fi

train=$1
version=$2

# The script's own directory, resolved before any cd: trains.sh always comes from the
# real checkout even when the tree being checked is a fixture somewhere else.
script_dir="$(cd "$(dirname "$0")" && pwd)"
root=${3:-$(cd "$script_dir/../.." && pwd)}

# shellcheck source=tools/trains.sh
. "$script_dir/../trains.sh"

# require_train names the train and lists the known ones on its own, so this adds no
# second message: two lines saying the same thing is how a reader learns to skim both.
if ! require_train "$train"; then
  exit 1
fi

cd "$root"

# The changelogs the train owns: one beside each of its projects, or the root file when
# its projects carry none.
files=''
for project in $(projects_of "$train"); do
  candidate="$(dirname "$project")/CHANGELOG.md"
  if [ -f "$candidate" ]; then
    files="$files $candidate"
  fi
done

if [ -z "$files" ] && [ -f CHANGELOG.md ]; then
  files=' CHANGELOG.md'
fi

if [ -z "$files" ]; then
  printf 'check-changelog: the %s train has no CHANGELOG.md, so the release it is about to publish is documented nowhere.\n' \
    "$train" >&2
  exit 1
fi

# The version reaches grep as a pattern, and a SemVer core is full of dots. Escaping is
# not cosmetic here: unescaped, 1.0.0 also matches 1x0y0, so a changelog documenting a
# neighbouring version would let the wrong release through.
escaped="$(printf '%s' "$version" | sed 's/[].[^$*\/\\]/\\&/g')"

# Keep a Changelog's dated form. The date is required rather than tolerated: an entry
# left as "## [1.0.0]" is one somebody opened and never closed, which is exactly the
# state this refuses to publish against.
pattern="^## \\[$escaped\\] - [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]"

status=0
for file in $files; do
  if grep -qE "$pattern" "$file"; then
    printf 'ok: %s documents %s\n' "$file" "$version"
  else
    printf 'check-changelog: %s carries no dated entry for %s.\n' "$file" "$version" >&2
    printf '  Add a heading of the form:  ## [%s] - YYYY-MM-DD\n' "$version" >&2
    printf '  An entry still under "## [Unreleased]" is the usual cause: the content is\n' >&2
    printf '  written and the heading still says it never shipped.\n' >&2
    status=1
  fi
done

exit "$status"
