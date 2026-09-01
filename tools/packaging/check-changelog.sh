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
# Read the project list LINE BY LINE, and keep the results newline-delimited. A space in a
# path — src/Kill Mutants/Core.csproj — would otherwise split into two nonexistent projects,
# whose changelogs are of course absent: the real one beside the real project would be ignored,
# and the root fallback could then authorise the release from an unrelated heading. Newline is
# the one separator a path cannot contain, so it is the one used throughout.
files=''
missing=''
while IFS= read -r project; do
  [ -n "$project" ] || continue
  candidate="$(dirname "$project")/CHANGELOG.md"
  if [ -f "$candidate" ]; then
    files="${files}${candidate}
"
  else
    missing="${missing}${project}
"
  fi
done <<PROJECTS
$(projects_of "$train")
PROJECTS

# A train is either wholly per-project or wholly on the root file — never half of each.
# Collecting only the changelogs that EXIST made $files non-empty as soon as one project had
# one, which skipped the root fallback for the others: a train with two packages and one
# changelog passed this check while the second package shipped documented nowhere. Since the
# entry proving a release is the thing being looked for, a project with no changelog at all
# is the one case that must never pass quietly.
if [ -n "$files" ] && [ -n "$missing" ]; then
  printf 'check-changelog: the %s train mixes per-project and absent changelogs.\n' "$train" >&2
  printf '  These of its projects keep one beside them:\n' >&2
  printf '%s' "$files" | sed 's|/CHANGELOG.md$||; s|^|    |' >&2
  printf '  These have none, so their packages would ship documented nowhere:\n' >&2
  printf '%s' "$missing" | sed 's|^|    |' >&2
  printf '  Give every project on the train its own CHANGELOG.md, or none of them (the root\n' >&2
  printf '  CHANGELOG.md then documents the train as a whole).\n' >&2
  exit 1
fi

# The root fallback may serve AT MOST ONE train. The trains version independently, so two of
# them can legitimately reach the same version number — and a single root heading carries no
# train identity, so `## [1.0.0]` written for the engine would silently authorise the CLI's
# 1.0.0 as well, which is precisely the undocumented release this script exists to refuse.
#
# A train contends for the root file only once it HAS projects and none of them keeps a
# changelog beside it. A train whose project does not exist yet is not a contender: it cannot
# be released at all (pack.sh refuses an empty train), and counting it would block the first
# real release of the train that IS ready.
if [ -z "$files" ] && [ -f CHANGELOG.md ]; then
  contenders=''
  for other in $(train_ids); do
    other_projects="$(projects_of "$other")"
    [ -n "$other_projects" ] || continue
    other_has_own=0
    while IFS= read -r project; do
      [ -n "$project" ] || continue
      if [ -f "$(dirname "$project")/CHANGELOG.md" ]; then other_has_own=1; break; fi
    done <<OTHER
${other_projects}
OTHER
    [ "$other_has_own" = 1 ] || contenders="$contenders $other"
  done
  # More than one contender means the root file cannot say which train an entry documents.
  if [ "$(printf '%s\n' $contenders | grep -c .)" -gt 1 ]; then
    printf 'check-changelog: the root CHANGELOG.md would have to document more than one train (%s),\n' \
      "$(printf '%s' "${contenders# }")" >&2
    printf '  and a version heading carries no train identity — so an entry written for one would\n' >&2
    printf '  authorise a release of the other at the same version. Give each of those trains a\n' >&2
    printf '  CHANGELOG.md beside its project.\n' >&2
    exit 1
  fi
  # Newline-terminated, like every other value of $files. A line without a trailing newline is
  # not read by `while read`, which silently emptied the loop below and passed every version.
  files='CHANGELOG.md
'
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

# The loop below runs in a subshell (a pipeline), so a variable assigned inside it would not
# survive. A marker file carries the verdict out instead.
_status_file="$(mktemp)"

# Keep a Changelog's dated form. The date is required rather than tolerated: an entry
# left as "## [1.0.0]" is one somebody opened and never closed, which is exactly the
# state this refuses to publish against.
pattern="^## \\[$escaped\\] - [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]"

status=0
printf '%s' "$files" | while IFS= read -r file; do
  [ -n "$file" ] || continue
  if grep -qE "$pattern" "$file"; then
    printf 'ok: %s documents %s\n' "$file" "$version"
  else
    printf 'check-changelog: %s carries no dated entry for %s.\n' "$file" "$version" >&2
    printf '  Add a heading of the form:  ## [%s] - YYYY-MM-DD\n' "$version" >&2
    printf '  An entry still under "## [Unreleased]" is the usual cause: the content is\n' >&2
    printf '  written and the heading still says it never shipped.\n' >&2
    printf '1' > "$_status_file"
  fi
done

[ -s "$_status_file" ] && status=1
rm -f "$_status_file"
exit "$status"
