#!/bin/sh
# Generate GitHub Release notes for ONE release train, containing only the commits that
# belong to that train — so an engine release never lists the tool's commits, and a tool
# release never announces an engine change.
#
# The partition is by Conventional Commit scope, and the scope-to-train mapping comes from
# tools/trains.sh, shared with the packaging script so the two can never disagree. Commits
# with no scope (bare `ci:`, `build:`, `chore:` ...) are infrastructure and are left out of
# every train: these notes describe what changed for the CONSUMER of the package, nothing
# else.
#
# Usage: tools/packaging/release-notes.sh <train> <current-tag> [<end-ref>]
#   Emits Markdown on stdout. Needs full history + tags in the checkout (actions/checkout
#   with fetch-depth: 0) so the previous same-train tag — the lower bound of the range —
#   resolves. <end-ref> is the upper bound and defaults to <current-tag>; pass the release
#   commit when the tag does not exist yet (a workflow_dispatch publish creates the tag
#   only after the notes are built).

set -eu

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ] || [ -z "$1" ] || [ -z "$2" ]; then
  echo "usage: tools/packaging/release-notes.sh <train> <current-tag> [<end-ref>]" >&2
  exit 2
fi
train="$1"
current_tag="$2"
# Upper bound of the commit range. Defaults to <current-tag>, but a caller that has not
# created the tag yet passes the release commit (e.g. $GITHUB_SHA) so `git log` resolves.
# <current-tag> is used only to exclude the tag being created from the previous-same-train-tag
# lookup, so it need not exist as a ref.
end_ref="${3:-$current_tag}"

# shellcheck source=tools/trains.sh
. "$(dirname "$0")/../trains.sh"
require_train "$train" || exit 2
prefix="$(prefix_of "$train")"
train_scopes="$(scopes_of "$train")"

# Previous tag of the SAME train (most recent one that is not the current tag). When there
# is none, this is the train's first release: take the whole history up to the end ref.
#
# This reads TAGS, and so takes a tag to mean "this was released". A tag left behind by a run
# that failed before publishing breaks that assumption: it becomes the lower bound, and the
# next release's notes then omit every commit up to it — changes no consumer ever received.
#
# The remedy is operational, not algorithmic: DELETE the tag of a release that published
# nothing (RELEASING.md, "When a release fails"). Nothing was published under it, so it
# describes an event that did not happen; deleting it restores the assumption for every later
# release, and costs one command at the moment the failure is already being handled.
#
# The alternative — resolving the previous SUCCESSFULLY published release — means asking
# nuget.org or the GitHub API which tags shipped. That trades a pure-git script, runnable and
# testable anywhere with no credentials, for one that needs the network and a token to say what
# a release contains. Not worth it for a case a `git tag -d` closes.
# Ordering matters twice here, and Git's default gets both wrong.
#
# `--sort=version:refname` places lib-v2.0.0-rc.1 ABOVE lib-v2.0.0 — measured — because it has
# no notion of a pre-release. `versionsort.suffix=-` supplies it: everything after a hyphen
# sorts before the bare version, which is SemVer precedence for the whole family
# (alpha < beta < rc < release).
#
# And "the highest tag that is not this one" is not the previous release: with lib-v2.1.0-rc.1
# already cut, a lib-v2.0.1 patch off a maintenance branch would take the RC as its lower
# bound and produce notes for a range that runs backwards. The previous release is the tag
# immediately BELOW this one in that ordering, which is what taking the line after it gives.
#
# The tag being released does not exist yet on a workflow_dispatch, so it is created locally,
# for the length of the lookup only, to be placed by the same comparator rather than by a
# second implementation of it. The trap removes it on every exit path.
_temp_tag=''
_cleanup() { [ -n "$_temp_tag" ] && git tag -d "$_temp_tag" >/dev/null 2>&1; return 0; }
trap _cleanup EXIT
if ! git rev-parse -q --verify "refs/tags/${current_tag}" >/dev/null 2>&1; then
  if git tag "$current_tag" "$end_ref" >/dev/null 2>&1; then _temp_tag="$current_tag"; fi
fi
# -A1 prints the current tag and the one after it; when it is already the oldest, -A1 yields
# only itself and the comparison below turns that into "no previous release".
previous_tag="$(git -c versionsort.suffix=- tag --list "${prefix}*" --sort=-version:refname \
  | grep -A1 -Fx "$current_tag" | tail -n1 || true)"
[ "$previous_tag" = "$current_tag" ] && previous_tag=''
_cleanup; _temp_tag=''; trap - EXIT
if [ -n "$previous_tag" ]; then
  range="${previous_tag}..${end_ref}"
else
  range="$end_ref"
fi

# One line per commit: "<short-hash><TAB><subject>". Merge commits are skipped — a pull
# request's merge commit carries no Conventional Commit scope; the real work lives in the
# commits it brings in.
commits="$(git log "$range" --no-merges --format='%h%x09%s')"

# Keep a commit only when its Conventional Commit scope list intersects this train's scopes.
# Header shape: type(scope[,scope...])[!]: description. A commit with no (scope) group is
# dropped.
notes=''
while IFS='	' read -r hash subject; do
  [ -z "${subject:-}" ] && continue
  # Extract the first parenthesised scope group ("feat(cli,core)!: ..." -> "cli,core");
  # empty when the header has no scope, which drops the commit.
  scope_group="$(printf '%s' "$subject" | sed -n 's/^[a-z][a-z]*(\([a-z,]*\)).*$/\1/p')"
  [ -z "$scope_group" ] && continue
  matched=0
  OLDIFS=$IFS; IFS=','
  for sc in $scope_group; do
    case ",${train_scopes}," in
      *",${sc},"*) matched=1; break ;;
      *) ;; # this scope does not belong to the train
    esac
  done
  IFS=$OLDIFS
  [ "$matched" = 1 ] && notes="${notes}- ${subject} (${hash})
"
done <<EOF
${commits}
EOF

echo "## What's changed"
echo
echo "This release publishes $(package_of "$train")."
echo
if [ -n "$notes" ]; then
  printf '%s' "$notes"
else
  echo "_No user-facing changes on this train._"
fi
