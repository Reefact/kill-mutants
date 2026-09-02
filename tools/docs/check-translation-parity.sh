#!/bin/sh
# Refuses a documentation change that leaves an English page and its French twin out of step.
#
#     tools/docs/check-translation-parity.sh [<base-ref>] [<root>]
#
# Exits 0 when every page under docs/ has its twin, when the two of a pair share a heading
# structure, when a decision record's status history reads the same in both languages, and when
# the two decision indexes list the same records. Given a base ref, it also refuses an English
# page changed without its French twin. Non-zero otherwise.
#
# ── Why this exists ───────────────────────────────────────────────────────────
# The documentation is bilingual and the English is canonical, which until now was a rule no tool
# refused. A change that edits one twin and forgets the other builds green, tests green and merges
# green; it is caught when a French reader reaches a page describing something the tool stopped
# doing. That reader was the check, and reading is not a check.
#
# ── What it cannot do ─────────────────────────────────────────────────────────
# It compares SHAPE, never meaning. A French twin rewritten to say something else passes here, and
# only a reader catches that. The gap is worth naming, because a check that looks thorough invites
# the belief that it is thorough: this proves the two files still have the same skeleton and still
# move together, not that they agree.
#
# ── Why one direction fails and the other warns ───────────────────────────────
# An English page changed without its French twin FAILS. A French page changed alone only warns.
# That asymmetry is the canonical-English rule restated: a French-only edit is a translation
# catching up, or an accent being fixed, and neither desynchronises anything. The reverse is the
# defect this exists to name. Refusing both directions would eventually go red over a corrected
# typo - and a gate that goes red for something that is not a defect teaches people to ignore
# gates, which is the one thing this repository refuses to spend.
#
# ── Four things a naive check gets wrong ──────────────────────────────────────
# Each of these was measured against the repository rather than guessed, and none of them is
# visible from the convention as stated:
#
#   - docs/decisions/README.md is the English twin of README-fr.md, with NO -en suffix. A
#     suffix-only rule calls it an orphan and flags 6.5% of this repository's own history.
#   - Headings are counted OUTSIDE fenced blocks only. A shell comment inside ``` is not a
#     heading, and most pages here carry fences.
#   - template-en.md and template-fr.md carry LOCALISED date placeholders - YYYY-MM-DD against
#     AAAA-MM-JJ - so the template is excluded from the status comparison rather than made to
#     match. A placeholder is not a date.
#   - The decision indexes are read from TABLE ROWS, never by scanning for identifiers. Both
#     READMEs discuss ADR-0001, ADR-0003 and ADR-0010 in prose, explaining the renumbering; a
#     free-text scan invents three index entries that match no file.
#
# ── Where it runs ─────────────────────────────────────────────────────────────
# ci.yml, in a job of its own, with no condition, on every event. It must NOT depend on the job
# that decides what to build: that job classifies docs/ and every *.md as documentation and skips
# the build, which is exactly the change this needs to see.
#
# Messages are plain prose on stderr rather than ::error:: annotations, because the script is also
# meant to be run by hand before pushing, and a workflow command printed at a terminal is noise.
set -eu

if [ $# -gt 2 ]; then
  printf 'usage: %s [base-ref] [root]\n' "$0" >&2
  exit 2
fi

base_ref=${1:-}

# The script's own directory, resolved before any cd, so the default root is the real checkout
# even when a tree somewhere else is being checked.
script_dir="$(cd "$(dirname "$0")" && pwd)"
root=${2:-$(cd "$script_dir/../.." && pwd)}

cd "$root"

if [ ! -d docs ]; then
  printf 'check-translation-parity: %s has no docs/ directory.\n' "$root" >&2
  exit 2
fi

status=0

fail() {
  printf 'check-translation-parity: %s\n' "$1" >&2
  status=1
}

note() {
  printf '  %s\n' "$1" >&2
}

# The level of every ATX heading, one per line, fenced blocks excluded. The level SEQUENCE is what
# gets compared: heading text is translated, so it cannot be.
#
# A fence closes only on the marker that opened it, at least as long, with nothing after it.
# Toggling on any line of three backticks or tildes was wrong twice over: a four-tick fence
# quoting a three-tick block, and a backtick fence quoting a tilde line, both left the parser
# believing it had returned to prose. A `#` in the quoted code then counted as a heading, and
# the comparison read a structure neither file has - which is worse than not checking, because
# it fails and passes for reasons that have nothing to do with the twins.
headings() {
  awk '
    {
      line = $0
      sub(/^[ \t]*/, "", line)
      ch = substr(line, 1, 1)

      if (ch == "`" || ch == "~") {
        n = 0
        while (substr(line, n + 1, 1) == ch) { n++ }

        if (n >= 3) {
          if (!fence) {
            fence = 1
            fence_ch = ch
            fence_len = n
            next
          }
          if (ch == fence_ch && n >= fence_len && substr(line, n + 1) ~ /^[ \t]*$/) {
            fence = 0
            next
          }
        }
      }

      if (fence) { next }

      if (substr($0, 1, 1) == "#") {
        n = 0
        while (substr($0, n + 1, 1) == "#") { n++ }
        c = substr($0, n + 1, 1)
        if (n <= 6 && (c == " " || c == "\t")) { print n }
      }
    }
  ' "$1"
}

# A record's status history, one row per line as `date canonical-status`. The section is found by
# position rather than by name - it is the first one in the mandatory format - because its heading
# is Status in one language and Statut in the other. The first two table lines are the header and
# its separator.
#
# The STATE is compared as well as the date, and that is not a refinement: comparing dates
# alone let an English record read Accepted while its French twin still read Proposé, which is
# exactly the divergence this check is advertised to catch. Since the two vocabularies differ
# by translation, each is mapped onto a common token; a state in neither vocabulary comes back
# marked `?`, so the caller can name it rather than compare two things it did not understand.
status_rows() {
  awk '
    function canonical(s) {
      if (s ~ /^Superseded by DEC[0-9][0-9][0-9][0-9]$/) { sub(/^Superseded by /, "", s); return "superseded:" s }
      if (s ~ /^Remplacé par DEC[0-9][0-9][0-9][0-9]$/)  { sub(/^Remplacé par /, "", s);  return "superseded:" s }
      if (s == "Proposed"   || s == "Proposé")  { return "proposed" }
      if (s == "Accepted"   || s == "Accepté")  { return "accepted" }
      if (s == "Rejected"   || s == "Rejeté")   { return "rejected" }
      if (s == "Deprecated" || s == "Déprécié") { return "deprecated" }
      return "?" s
    }
    function trim(s) { gsub(/^[ \t]+/, "", s); gsub(/[ \t]+$/, "", s); return s }

    /^## /               { section++; next }
    section == 1 && /^\|/ {
      row++
      if (row > 2) {
        split($0, cell, "|")
        print trim(cell[2]) " " canonical(trim(cell[3]))
      }
    }
  ' "$1"
}

# Anchored to the table row, never a free-text scan: see the fourth trap above.
index_ids() {
  sed -n 's/^| \[\(DEC[0-9][0-9][0-9][0-9]\)\].*/\1/p' "$1" | LC_ALL=C sort
}

# The English half of a pair: the suffixed name when it exists, otherwise the bare one, which is
# how docs/decisions/README.md pairs with README-fr.md.
english_of() {
  if [ -f "$1-en.md" ]; then
    printf '%s-en.md' "$1"
  else
    printf '%s.md' "$1"
  fi
}

# One stem per pair. Stripping all three suffixes collapses both halves onto the same stem, and
# sort -u leaves one line per pair however the halves are named.
stems="$(find docs -type f -name '*.md' \
  | sed -e 's/-en\.md$//' -e 's/-fr\.md$//' -e 's/\.md$//' \
  | LC_ALL=C sort -u)"

pairs=0

while IFS= read -r stem; do
  [ -n "$stem" ] || continue

  en="$(english_of "$stem")"
  fr="$stem-fr.md"

  if [ ! -f "$en" ]; then
    fail "$fr has no English twin."
    note "Expected $stem-en.md, or $stem.md for a page that predates the suffix."
    continue
  fi

  if [ ! -f "$fr" ]; then
    fail "$en has no French twin."
    note "The English is canonical, but it does not travel alone: write $fr in the same pass."
    continue
  fi

  pairs=$((pairs + 1))

  if [ "$(headings "$en")" != "$(headings "$fr")" ]; then
    fail "$en and $fr no longer share a heading structure."
    note "One of them gained, lost or moved a section. Compare their headings; the texts differ"
    note "by translation, but the shape does not get to."
  fi

  # Records only, and not the template: its date placeholders are localised, so the two halves are
  # correctly different and comparing them would refuse a file that is right.
  case "$stem" in
    docs/decisions/[0-9][0-9][0-9][0-9]-*)
      en_rows="$(status_rows "$en")"
      fr_rows="$(status_rows "$fr")"

      # A state in neither vocabulary is named rather than silently compared: two files can
      # agree on a word this script does not know, and passing then would report an agreement
      # it never checked.
      unknown="$(printf '%s\n%s\n' "$en_rows" "$fr_rows" | sed -n 's/^[^ ]* ?\(.*\)$/\1/p' | LC_ALL=C sort -u)"
      if [ -n "$unknown" ]; then
        fail "$en or $fr uses a status this check does not know: $(printf '%s' "$unknown" | tr '\n' ' ')"
        note "The vocabulary is listed in docs/decisions/README.md. If the base has gained a state,"
        note "teach it to canonical() in this script; if it has not, this is a typo in a record."
      elif [ "$en_rows" != "$fr_rows" ]; then
        fail "$en and $fr disagree about the record's status history."
        note "A status row is append-only history and belongs to the decision, not to a language:"
        note "both halves carry the same rows, the same dates and the same states, in the same order."
      fi
      ;;
    *) ;;
  esac
done <<STEMS
$stems
STEMS

if [ -f docs/decisions/README.md ] && [ -f docs/decisions/README-fr.md ]; then
  if [ "$(index_ids docs/decisions/README.md)" != "$(index_ids docs/decisions/README-fr.md)" ]; then
    fail "the two decision indexes do not list the same records."
    note "docs/decisions/README.md and README-fr.md index the same base; a record added to one"
    note "and not the other is invisible to half the readership."
  fi
fi

if [ -n "$base_ref" ]; then
  if ! git rev-parse --verify --quiet "$base_ref" > /dev/null; then
    printf 'check-translation-parity: %s is not a revision this checkout knows.\n' "$base_ref" >&2
    note "A shallow clone is the usual cause: the workflow needs fetch-depth: 0 for the base"
    note "branch to be present."
    exit 2
  fi

  changed="$(git diff --no-renames --name-only "$base_ref...HEAD" -- docs)"

  while IFS= read -r file; do
    [ -n "$file" ] || continue

    case "$file" in
      *-fr.md)
        twin="$(english_of "${file%-fr.md}")"
        if ! printf '%s\n' "$changed" | grep -qxF "$twin"; then
          printf 'check-translation-parity: warning: %s changed without %s.\n' "$file" "$twin" >&2
          note "Allowed - the English is canonical, so the French may catch up on its own. Said"
          note "out loud in case the English was meant to move too."
        fi
        ;;
      *-en.md)
        twin="${file%-en.md}-fr.md"
        if ! printf '%s\n' "$changed" | grep -qxF "$twin"; then
          fail "$file changed without $twin."
          note "The French twin changes in the same pass. Nothing else will notice that it did not."
        fi
        ;;
      *.md)
        twin="${file%.md}-fr.md"
        # A bare name with no French twin is not half of a pair, and the existence check above has
        # already had its say about whether it should be.
        if [ -f "$twin" ] && ! printf '%s\n' "$changed" | grep -qxF "$twin"; then
          fail "$file changed without $twin."
          note "The French twin changes in the same pass. Nothing else will notice that it did not."
        fi
        ;;
      *) ;;
    esac
  done <<CHANGED
$changed
CHANGED
fi

if [ "$status" -eq 0 ]; then
  printf 'ok: %d twin pairs under docs/ are in step\n' "$pairs"
fi

exit "$status"
