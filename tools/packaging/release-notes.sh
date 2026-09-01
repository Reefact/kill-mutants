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
previous_tag="$(git tag --list "${prefix}*" --sort=-version:refname | grep -Fxv "$current_tag" | head -n1 || true)"
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
