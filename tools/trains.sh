#!/bin/sh
# Single source of truth for the release trains.
#
# The published trains version independently and each owns a tag prefix, a set of
# Conventional Commit scopes, and a package label. That mapping lives here, once;
# the packaging and release-notes scripts SOURCE this file, so what a release
# publishes and what its notes describe can never drift apart.
#
# This file is meant to be SOURCED (`. tools/trains.sh`), not executed — it only
# defines functions and mutates nothing.
#
# ── Why trains, and why these two ────────────────────────────────────────────
# Stryker.NET — the reference implementation of this problem space — publishes its
# engine (`stryker`) and its CLI (`dotnet-stryker`) from one repository under a
# SINGLE version, bumped by hand in three files. The engine therefore ships a new
# number every time the CLI does, whether or not a line of it changed. Trains are
# the alternative: the engine and the tool are versioned, tagged, released and
# described independently, and a release of one never republishes the other.
#
# ── Which PROJECTS a train publishes ─────────────────────────────────────────
# Not listed here. A project joins a train by declaring it in its own .csproj:
#
#     <PropertyGroup>
#       <ReleaseTrain>cli</ReleaseTrain>
#     </PropertyGroup>
#
# and `projects_of` below discovers it. Membership therefore lives in the one file
# that cannot be forgotten when the project is created, moved or renamed. Declaring
# the train is also what makes a project packable and what gives it an embedded
# SBOM; see Directory.Build.targets.
#
# ── Adding a train ───────────────────────────────────────────────────────────
# 1. add one row to trains_rows() below;
# 2. add its tag pattern to the `on: push: tags:` list and its id to the
#    workflow_dispatch choice in .github/workflows/release.yml — GitHub requires
#    both to be literal, so they cannot be derived from this file.
# A tag whose prefix is unknown here is rejected by the release workflow, so a
# missed step 1 fails the release rather than publishing something unrouted.
#
# Row format (pipe-separated, no spaces around the pipes except inside the label):
#   <id>|<tag-prefix>|<scopes csv>|<package label>
#
# The scopes decide which commits reach a train's release notes. A commit whose
# scope appears on no row is silently dropped from every set of notes, so a scope
# added to the commit convention belongs on a row here the same day. Keep this set
# and whatever the commit linter accepts naming the SAME scopes: that equality is
# what makes "the notes describe the release" true rather than hopeful.
trains_rows() {
  cat <<'ROWS'
lib|lib-v|core,mutators,reporters|the KillMutants engine
cli|cli-v|cli|the KillMutants CLI (a .NET tool)
ROWS
}

# _train_field <id> <field-name> — echo one field of a train's row, or nothing if
# the id is unknown. Fields: prefix | scopes | package.
_train_field() {
  _tf_id="$1"; _tf_field="$2"
  trains_rows | while IFS='|' read -r id prefix scopes package; do
    [ "$id" = "$_tf_id" ] || continue
    case "$_tf_field" in
      prefix)  printf '%s\n' "$prefix" ;;
      scopes)  printf '%s\n' "$scopes" ;;
      package) printf '%s\n' "$package" ;;
      # A caller asking for a field this row format does not carry is a bug in the caller, not a
      # missing value: say so on stderr rather than returning the empty string an unknown TRAIN
      # returns, which require_train reads as "no such train".
      *)       printf 'trains.sh: unknown field "%s"\n' "$_tf_field" >&2 ;;
    esac
  done
}

train_ids()  { trains_rows | cut -d'|' -f1; }
prefix_of()  { _train_field "$1" prefix; }
scopes_of()  { _train_field "$1" scopes; }
package_of() { _train_field "$1" package; }

# require_train <id> — succeed if <id> is a known train, else print the known ids
# to stderr and return 1. Callers decide the exit code.
require_train() {
  if [ -n "$(prefix_of "$1")" ]; then
    return 0
  fi
  printf 'unknown train "%s" (known: %s)\n' \
    "$1" "$(train_ids | tr '\n' ' ' | sed 's/ *$//')" >&2
  return 1
}

# train_of_tag <tag> — echo the train id a release tag belongs to, or nothing.
# Matches the tag against every known prefix rather than a hardcoded case, so a
# train added to trains_rows is routed without touching the release workflow's
# script. The longest matching prefix wins, so a prefix that is a prefix of
# another can never shadow it.
train_of_tag() {
  _tot_tag="$1"; _tot_best=''; _tot_len=0
  for _tot_id in $(train_ids); do
    _tot_prefix="$(prefix_of "$_tot_id")"
    case "$_tot_tag" in
      "${_tot_prefix}"*)
        if [ "${#_tot_prefix}" -gt "$_tot_len" ]; then
          _tot_best="$_tot_id"; _tot_len="${#_tot_prefix}"
        fi
        ;;
      *) ;; # this train's prefix does not match the tag
    esac
  done
  # Always succeed, even when nothing matched: callers read the RESULT through a
  # command substitution, and an assignment inheriting a non-zero status would
  # abort a `set -e` caller before it could print its own diagnostic.
  if [ -n "$_tot_best" ]; then printf '%s\n' "$_tot_best"; fi
  return 0
}

# _without_xml_comments <path> — echo the file with every <!-- ... --> region removed,
# including a region spanning several lines.
#
# Membership is a fact about what a project DECLARES, and an element shown inside a
# comment declares nothing. Without this, writing <ReleaseTrain>cli</ReleaseTrain>
# in a comment — to say what a project will join later, or why it has not joined yet —
# enrols it for real: it becomes packable, and a release publishes it.
#
# Line-oriented tools cannot do this: a comment opened on one line and closed on another
# is invisible to grep and to sed. awk carries the state across lines.
_without_xml_comments() {
  awk '
    {
      _line = $0; _out = ""
      while (length(_line) > 0) {
        if (_inside) {
          _at = index(_line, "-->")
          if (_at == 0) { _line = "" } else { _line = substr(_line, _at + 3); _inside = 0 }
        } else {
          _at = index(_line, "<!--")
          if (_at == 0) { _out = _out _line; _line = "" }
          else { _out = _out substr(_line, 1, _at - 1); _line = substr(_line, _at + 4); _inside = 1 }
        }
      }
      print _out
    }
  ' "$1"
}

# _flattened <path> — echo the file with every comment removed and every newline turned into
# a space, so an element written across several lines matches as a single one.
#
# MSBuild does not care where the line breaks fall. Both of these are ordinary XML and it
# reads both:
#
#     <ReleaseTrain>          <ProjectReference
#       lib                     Include="../Core/Core.csproj" />
#     </ReleaseTrain>
#
# A line-oriented grep or sed sees neither. That is not a cosmetic gap: a project MSBuild
# considers to be on a train would be left out of its own release, and the release would stay
# green — the exact failure the discover-don't-list design exists to prevent. Verified with
# `dotnet msbuild -getProperty:ReleaseTrain`, which reads "      lib" from the form above.
_flattened() {
  _without_xml_comments "$1" | tr '\n' ' '
}

# projects_of <id> — echo the .csproj paths that declare this train, one per line.
# Empty output means the train publishes nothing yet, which is a normal state for
# a train whose project has not been created — and is exactly the state this
# repository is in until the first project lands.
#
# bin/ and obj/ are skipped, because what a train publishes must be read from the
# SOURCE tree alone. A project file copied into a build output is an ordinary
# .csproj to a tree-wide grep, and the copy would be packed: `dotnet pack` gets a
# path with no restore behind it and fails the release rehearsal, and a copy that
# HAD been restored would publish the same package twice from one train.
projects_of() {
  # The cheap tree-wide filter matches the OPENING TAG only — that much is always on one
  # line — and the precise test then runs on the flattened file, so a declaration split
  # across lines survives the filter instead of being dropped by it. Only files that mention
  # the element at all pay for the second pass.
  grep -rl "<ReleaseTrain" \
    --include='*.csproj' --exclude-dir=bin --exclude-dir=obj . 2>/dev/null \
    | sed 's|^\./||' | sort | while read -r _po_proj; do
    if _flattened "$_po_proj" \
         | grep -q -E "<ReleaseTrain[^>]*>[[:space:]]*$1[[:space:]]*</ReleaseTrain>"; then
      printf '%s\n' "$_po_proj"
    fi
  done
  # Always succeed, for the reason train_of_tag does: callers read the RESULT through a
  # command substitution, and `x="$(projects_of lib)"` in a `set -e` script would abort on
  # the non-zero status the leading grep returns when it matches nothing — which is every
  # repository that has no .csproj yet, this one included. Emptiness is an answer here, not
  # a failure. Measured: without this, the rehearsal died before reaching its own checks.
  return 0
}

# ambiguous_trains — echo the .csproj paths declaring <ReleaseTrain> more than once.
#
# MSBuild keeps the LAST value; a textual predicate answers yes for every one of them, so a
# project writing lib and then cli is reported as being on both trains at once. Two independently
# versioned releases would then pack and publish the same package identity, each believing it
# owns it. Refused for the reason a conditional declaration is: membership has to have exactly
# one answer, and guessing which of two the author meant is not one.
ambiguous_trains() {
  grep -rl "<ReleaseTrain" \
    --include='*.csproj' --exclude-dir=bin --exclude-dir=obj . 2>/dev/null \
    | sed 's|^\./||' | sort | while read -r _at_proj; do
    if [ "$(_flattened "$_at_proj" | grep -o -E "<ReleaseTrain[^>]*>" | grep -c .)" -gt 1 ]; then
      printf '%s\n' "$_at_proj"
    fi
  done
  return 0   # empty is an answer; see projects_of
}

# declared_trains — echo every train id declared by a .csproj anywhere in the
# tree, one per line, deduplicated. Used to catch a value that matches no train:
# such a project would simply never be packed, silently, and a typo in a property
# nothing validates is exactly the kind of mistake that surfaces at release time.
declared_trains() {
  # Build outputs are skipped here for the reason given on projects_of: a value only a
  # copy declares would be reported as if a project had chosen it.
  grep -rl "<ReleaseTrain" \
    --include='*.csproj' --exclude-dir=bin --exclude-dir=obj . 2>/dev/null \
    | while read -r _dt_proj; do _flattened "$_dt_proj"; done \
    | grep -o -E "<ReleaseTrain[^>]*>[^<]*</ReleaseTrain>" \
    | sed -E 's|.*<ReleaseTrain[^>]*>[[:space:]]*([^<[:space:]]*)[[:space:]]*</ReleaseTrain>.*|\1|' \
    | sort -u || true
}

# conditioned_trains — echo the .csproj paths whose <ReleaseTrain> carries any attribute.
#
# Membership is an identity, not a build option: a project either ships on a train or it does
# not. A Condition would make that depend on the configuration being evaluated, and no
# text-based discovery can answer it — matching the element would over-count a project whose
# condition is false, and ignoring the element would silently drop one whose condition is true.
# Both are wrong in a way nothing downstream could detect, so the declaration is refused
# instead, which is the one answer that is never wrong.
conditioned_trains() {
  grep -rl "<ReleaseTrain" \
    --include='*.csproj' --exclude-dir=bin --exclude-dir=obj . 2>/dev/null \
    | sed 's|^\./||' | sort | while read -r _ct_proj; do
    if _flattened "$_ct_proj" | grep -q -E "<ReleaseTrain[[:space:]][^>]*>"; then
      printf '%s\n' "$_ct_proj"
    fi
  done
  return 0   # empty is an answer; see projects_of
}
