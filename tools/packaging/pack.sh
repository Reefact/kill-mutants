#!/bin/sh
# Single source of truth for producing the published NuGet packages.
#
# Both the real release (.github/workflows/release.yml) and the automatic
# rehearsal (.github/workflows/release-dryrun.yml) call this, so the dry run can
# never silently drift from the release it is meant to mirror: the packed
# projects, the pack flags, the embedded SBOM and the guards all live here, once.
#
# It writes the .nupkg / .snupkg into ./artifacts.
#
# Usage: tools/packaging/pack.sh <version> <train>
#   <version> is any valid SemVer (a real release passes the tag's version; the
#             dry run passes a throwaway like 0.0.0-dryrun).
#   <train>   selects which release train to pack. The trains version and publish
#             independently; the known ids come from tools/trains.sh, and the
#             projects on a train are the ones declaring it (see below).

set -eu

if [ "$#" -ne 2 ] || [ -z "$1" ] || [ -z "$2" ]; then
  echo "usage: tools/packaging/pack.sh <version> <train>" >&2
  exit 2
fi
version="$1"
train="$2"

# shellcheck source=tools/trains.sh
. "$(dirname "$0")/../trains.sh"
require_train "$train" || exit 2

# --- membership ---------------------------------------------------------------
# A project joins a train by declaring <ReleaseTrain> in its own .csproj, and is
# discovered here. Nothing lists the projects twice, so a renamed or moved project
# cannot fall out of a release by being forgotten in a second file.

# A declared train that matches no row in trains.sh publishes nothing, silently:
# the project is simply never discovered. A typo in a property nothing validates
# is exactly the mistake that surfaces on release day, so reject it on every pack
# — including the dry run, which is what makes it surface on an ordinary pull
# request instead.
unknown=''
for declared in $(declared_trains); do
  if [ -z "$(prefix_of "$declared")" ]; then
    unknown="${unknown} ${declared}"
  fi
done
if [ -n "$unknown" ]; then
  echo "error: project(s) declare unknown release train(s):${unknown}" >&2
  echo "       known trains: $(train_ids | tr '\n' ' ' | sed 's/ *$//')" >&2
  echo "       fix the <ReleaseTrain> value, or add the train to tools/trains.sh." >&2
  exit 1
fi

# An attribute on <ReleaseTrain> — a Condition, above all — is refused rather than guessed at.
# See conditioned_trains in trains.sh for why neither reading is safe.
ambiguous="$(ambiguous_trains)"
if [ -n "$ambiguous" ]; then
  echo "error: <ReleaseTrain> is declared more than once in:" >&2
  printf '%s\n' "$ambiguous" | sed 's|^|  - |' >&2
  echo "       MSBuild keeps the last value, but a project reported on two trains would be packed" >&2
  echo "       and published by both. Declare the train exactly once." >&2
  exit 1
fi

conditioned="$(conditioned_trains)"
if [ -n "$conditioned" ]; then
  echo "error: <ReleaseTrain> carries an attribute in:" >&2
  printf '%s\n' "$conditioned" | sed 's|^|  - |' >&2
  echo "       Release-train membership is an identity, not a build option, and a conditional" >&2
  echo "       declaration cannot be resolved from the project text. Declare it unconditionally." >&2
  exit 1
fi

projects="$(projects_of "$train")"
if [ -z "$projects" ]; then
  echo "error: no project declares <ReleaseTrain>${train}</ReleaseTrain>; there is nothing to publish on this train." >&2
  exit 1
fi

# --- the repository's package identities, evaluated ------------------------------
# One "<PackageId>|<ReleaseTrain>" line per project. Read from MSBuild rather than from the
# project text: PackageId may be defaulted by the SDK to the project name, set in an imported
# .props, or written as a property expression, and every spelling reaches the nuspec while a
# text scan reads something else.
#
# -p:Configuration=Release because that is what the pack below uses. Evaluating in the default
# Debug would read a different value for any property conditioned on the configuration, and the
# whole point of asking MSBuild is to be told what the RELEASE package will say.
# Asked for TWO properties at a time, because MSBuild answers both from one evaluation and each
# evaluation costs about half a second — measured. Per pack this is one call per project in the
# repository plus two per project on the train, rather than twice and three times that; on a
# twenty-project repository the difference is roughly half a minute, on every pull request.
#
# With two or more names MSBuild replies in JSON; with a single name it replies with the bare
# value. Everything here asks for two, so only the JSON shape has to be read.
_project_properties() {
  _pp_proj="$1"; _pp_a="$2"; _pp_b="$3"
  dotnet msbuild "$_pp_proj" -getProperty:"$_pp_a" -getProperty:"$_pp_b" \
    -p:Configuration=Release -nologo 2>/dev/null | tr -d '\r\n'
}
# _json_value <json> <name> — the string value of one key, empty when absent or empty.
_json_value() {
  printf '%s' "$1" | grep -oE "\"$2\": \"[^\"]*\"" | head -n1 | sed 's|.*": "||; s|"$||'
}

local_map=''
find . -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print > "${TMPDIR:-/tmp}/km-projects.$$"
while IFS= read -r _proj; do
  [ -n "$_proj" ] || continue
  _json="$(_project_properties "$_proj" PackageId ReleaseTrain)"
  _id="$(_json_value "$_json" PackageId)"
  [ -n "$_id" ] || continue
  local_map="${local_map}${_id}|$(_json_value "$_json" ReleaseTrain)|${_proj}
"
done < "${TMPDIR:-/tmp}/km-projects.$$"
rm -f "${TMPDIR:-/tmp}/km-projects.$$"

# Package identities are compared as LITERAL, CASE-FOLDED strings, everywhere below.
#
# Literal, because a package id is full of dots: interpolating one into a regular expression
# turns each into a wildcard, and an external `Foo.Bar` is then mistaken for a local `FooXBar`.
#
# Case-folded, because nuget.org is case-insensitive while a Linux filename and a plain `sort`
# are not: KillMutants.Core and killmutants.core are one package to the feed and two to the
# shell. A duplicate that differs only in case is still a duplicate, and a dependency written
# in another casing is still the same dependency.
_fold() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }

# _local_train <package-id> — the train of the local project owning that id, or nothing.
_local_train() {
  _lt_want="$(_fold "$1")"
  printf '%s\n' "$local_map" | while IFS='|' read -r _lt_id _lt_train _lt_proj; do
    [ "$(_fold "$_lt_id")" = "$_lt_want" ] && { printf '%s\n' "$_lt_train"; break; }
  done
  return 0
}
# _local_project <package-id> — the .csproj path of the local project owning that id.
_local_project() {
  _lp_want="$(_fold "$1")"
  printf '%s\n' "$local_map" | while IFS='|' read -r _lp_id _lp_train _lp_proj; do
    [ "$(_fold "$_lp_id")" = "$_lp_want" ] && { printf '%s\n' "$_lp_proj"; break; }
  done
  return 0
}
# _is_local <package-id> — true when some project in this repository publishes that id.
_is_local() {
  printf '%s\n' "$local_map" | cut -d'|' -f1 | tr '[:upper:]' '[:lower:]' | grep -Fxq "$(_fold "$1")"
}

# --- guard: no two projects claim the same package identity ----------------------
# Checked across the WHOLE repository, not within the packed train. Two projects on ONE train
# overwrite each other's .nupkg, which the per-train count below catches; two projects on
# DIFFERENT trains each pack cleanly on their own run, and the two independently versioned
# releases then publish different artifacts under one nuget.org identity — the second either
# overwriting the story of the first or landing as a --skip-duplicate no-op. No count taken
# inside a single train can see that.
duplicate_ids="$(printf '%s\n' "$local_map" | cut -d'|' -f1 | grep -v '^$' \
  | tr '[:upper:]' '[:lower:]' | sort | uniq -d)"
if [ -n "$duplicate_ids" ]; then
  echo "error: more than one project resolves to the same PackageId:" >&2
  printf '%s\n' "$duplicate_ids" | while IFS= read -r _dup; do
    [ -n "$_dup" ] || continue
    printf '  - %s\n' "$_dup" >&2
    printf '%s\n' "$local_map" | while IFS='|' read -r _id _tr _pr; do
      [ "$(_fold "$_id")" = "$_dup" ] && printf '      %s as %s (train %s)\n' "$_pr" "$_id" "${_tr:-none}" >&2
    done
  done
  echo "       A package identity belongs to exactly one project; give each its own PackageId." >&2
  echo "       Compared case-insensitively, because nuget.org is." >&2
  exit 1
fi

# Every loop over the project list reads it LINE BY LINE. `for project in $projects` word-splits,
# so a project under a path containing a space — src/Kill Mutants/Core.csproj — would reach
# dotnet as two nonexistent paths and fail the whole train. The list is newline-delimited by
# construction (projects_of prints one per line), which is the only separator a path cannot hold.
echo "Packing the '${train}' train at ${version}:"
printf '%s\n' "$projects" | while IFS= read -r project; do
  [ -n "$project" ] && echo "  ${project}"
done

# --- guard: the SDK stamps the assembly version ----------------------------------
# The assembly/package version guard in Directory.Build.targets compares two MSBuild PROPERTIES.
# It cannot see an attribute a project emits itself: with GenerateAssemblyInfo (or
# GenerateAssemblyVersionAttribute) turned off, $(AssemblyVersion) can still agree with
# $(Version) while the compiled binary carries something else, and the package ships an assembly
# identity that disagrees with its number — precisely the failure that guard exists to prevent,
# in the one shape invisible to it.
#
# Refused rather than worked around. Reading the version out of a compiled assembly means parsing
# PE metadata in shell, and the property comparison would still be what decides; requiring the SDK
# to stamp the attribute is a rule that can be checked exactly, and costs a train project nothing
# it should want.
hand_stamped=''
while IFS= read -r project; do
  [ -n "$project" ] || continue
  json="$(_project_properties "$project" GenerateAssemblyInfo GenerateAssemblyVersionAttribute)"
  for prop in GenerateAssemblyInfo GenerateAssemblyVersionAttribute; do
    case "$(_fold "$(_json_value "$json" "$prop")")" in
      false) hand_stamped="${hand_stamped}  - ${project} sets ${prop}=false
" ;;
      *) ;;   # unset or true: the SDK stamps it, and the targets guard can see it
    esac
  done
done <<PROJECTS
${projects}
PROJECTS
if [ -n "$hand_stamped" ]; then
  echo "error: a project on the '${train}' train stamps its own assembly version:" >&2
  printf '%s' "$hand_stamped" >&2
  echo "       Then nothing can prove the assembly matches the package version it ships under." >&2
  echo "       Let the SDK generate the attribute from \$(Version), which the release sets." >&2
  exit 1
fi
echo "ok: the SDK stamps the assembly version on every project of the '${train}' train"

# --- pack ---------------------------------------------------------------------
# GenerateSBOM activates Microsoft.Sbom.Targets (wired in Directory.Build.targets for every
# project declaring a train): each package embeds its SPDX inventory at
# _manifest/spdx_2.2/manifest.spdx.json. It is passed here, not set in the project files, so
# an ordinary local `dotnet pack` stays SBOM-free and fast.
#
# This COMPILES rather than packing whatever bin/ happens to hold, and the difference is not
# academic: `dotnet pack --no-build` writes the package version from -p:Version whether or not
# the assembly beside it agrees, so a pack over stale output will faithfully number anything.
# A test step that itself packs something — a packaging integration test restoring the engine
# like a consumer would — rebuilds into the shared bin/Release, and a --no-build release would
# then ship those leftovers under the release's number. Compiling here removes the whole class:
# no step that ran earlier can decide what a release ships. The build is deterministic, so
# recompiling the same source at the same version reproduces the bytes the test step exercised.
#
# Start from an EMPTY output directory. Everything downstream globs artifacts/*.nupkg —
# the SBOM guard here, the provenance attestation, and `dotnet nuget push`, which is
# irreversible — so any package that happens to be sitting there is attested, attached to
# this train's GitHub Release and published under it. That is not hypothetical: the step
# order above is build, TEST, pack, and a packaging test that restores a package like a
# consumer would packs one itself. A stale artifact is also exactly what the other train's
# rehearsal leaves behind when both are packed in one job.
#
# Removing rather than reusing: the point is that what this script publishes is only ever
# what THIS invocation produced, for the train it was given.
rm -rf artifacts
mkdir -p artifacts

printf '%s\n' "$projects" | while IFS= read -r project; do
  [ -n "$project" ] || continue
  dotnet pack "$project" -c Release -p:Version="$version" -p:GenerateSBOM=true -o artifacts
done

# --- guard: one distinct package per project ------------------------------------
# Two projects on a train that resolve to the same PackageId pack to the same
# <id>.<version>.nupkg path, so the second overwrites the first and every later guard still
# sees a valid artifact. The release then succeeds having silently dropped a declared
# project. Counting is enough to catch it, and says so before anything is attested.
expected="$(printf '%s\n' "$projects" | grep -c .)"
produced="$(ls artifacts/*.nupkg 2>/dev/null | wc -l)"
if [ "$produced" -ne "$expected" ]; then
  echo "error: the '${train}' train has ${expected} project(s) but packing produced ${produced} package(s)." >&2
  echo "       Two projects resolving to the same PackageId overwrite each other's .nupkg; give each its own." >&2
  exit 1
fi
echo "ok: ${produced} package(s) for ${expected} project(s) on the '${train}' train"

# --- guard: no dependency on a package that will never exist ---------------------
# Read from the PRODUCED nuspec. This is the ONLY check on what a package depends on, and it
# replaced a text scan of the project files that read every ProjectReference itself.
#
# That scan was both redundant and wrong. Redundant: everything it could catch reaches the
# nuspec, which is what actually ships. Wrong in two directions — it could not see a reference
# contributed by an imported .props, written under a Condition, split across lines, or quoted
# with apostrophes (four separate defects, each found only after the last was fixed), and it
# REFUSED a legitimate one: a cross-train ProjectReference carrying PrivateAssets="all" emits an
# empty dependency group — measured — so the package depends on nothing and is perfectly sound,
# while the scan called it a violation.
#
# The general shape is worth keeping in mind: a check on the INPUT can be outrun by a new way of
# writing the input, and every new spelling is a new defect. A check on the ARTIFACT cannot.
#
# The failure it closes: `dotnet pack` represents a ProjectReference as a package DEPENDENCY at
# the version being packed — it does not embed the referenced assembly. A train project
# referencing an ordinary helper therefore ships <dependency id="Helper" version="1.2.3" /> for
# a package nothing publishes, and every consumer restore fails (NU1101) against an immutable
# artifact. Measured: that nuspec is exactly what a plain helper reference produces.
#
# Which dependencies are refused depends on what the referenced project IS, so the verdict is
# taken per train, not per id:
#
#   on no train        refuse always — nothing will ever publish it;
#   on THIS train      allowed, since it is co-published at this very version (and the count
#                      guard above already proved every project on the train produced a package);
#   on ANOTHER train   refused when a ProjectReference reaches it from a project being packed,
#                      because that is what stamps the dependency at the version being packed —
#                      a version of the other train no release ever produced. Reached instead
#                      through a PackageReference, it is the prescribed cross-train mechanism
#                      and is allowed.
#
# How the two are told apart matters, and the obvious shortcut is wrong. Comparing the dependency
# version to the version being packed looks like it identifies a ProjectReference, and does not:
# independently versioned trains reach the same number all the time — most obviously at the FIRST
# release of both, where cli 1.0.0 legitimately depends on the published lib 1.0.0. That heuristic
# blocked exactly that release. The reference kind is asked of MSBuild instead, which also answers
# for a reference contributed by an imported .props or written under a Condition — neither of which
# any reading of the project text can see.
_referenced_projects() {
  dotnet msbuild "$1" -getItem:ProjectReference -p:Configuration=Release -nologo 2>/dev/null \
    | grep -oE '"FullPath": "[^"]*"' | sed 's|.*: "||; s|"$||'
  return 0
}

# The absolute paths of every project reachable by ProjectReference from anything this pack
# builds. Compared as paths rather than ids, so a project whose PackageId is defaulted or
# computed is still recognised as the one being referenced.
stamped_projects="$(
  while IFS= read -r project; do
    [ -n "$project" ] || continue
    _abs_project="$(cd "$(dirname "$project")" && pwd)/$(basename "$project")"
    _referenced_projects "$_abs_project"
  done <<PROJECTS2
${projects}
PROJECTS2
)"

# Folded on the way in, so the membership test below is case-insensitive like every other
# identity comparison here. Without it a dependency spelled killmutants.core would not match
# the KillMutants.Core this very pack produced, and would be reported as a phantom.
produced_ids=''
for package in artifacts/*.nupkg; do
  produced_ids="${produced_ids}$(_fold "$(unzip -p "$package" '*.nuspec' | tr '\n' ' ' \
    | grep -oE '<id>[^<]*</id>' | head -n1 | sed 's|</\?id>||g')")
"
done

phantom=''
for package in artifacts/*.nupkg; do
  nuspec="$(unzip -p "$package" '*.nuspec' 2>/dev/null | tr '\n' ' ')"
  for dep in $(printf '%s' "$nuspec" | grep -oE '<dependency id="[^"]*" version="[^"]*"' \
                 | sed 's|<dependency id="||; s|" version="|@|; s|"$||'); do
    dep_id="${dep%@*}"; dep_version="${dep#*@}"
    _is_local "$dep_id" || continue                                   # not ours: ordinary dependency
    printf '%s\n' "$produced_ids" | grep -Fxq "$(_fold "$dep_id")" && continue  # co-published here
    dep_train="$(_local_train "$dep_id")"
    dep_project="$(_local_project "$dep_id")"
    dep_abs=''
    [ -n "$dep_project" ] && dep_abs="$(cd "$(dirname "$dep_project")" 2>/dev/null && pwd)/$(basename "$dep_project")"
    if [ -z "$dep_train" ]; then
      phantom="${phantom}  - $(basename "$package") -> ${dep_id} ${dep_version} (on no release train: nothing publishes it)
"
    elif [ -n "$dep_abs" ] && printf '%s\n' "$stamped_projects" | grep -Fxq "$dep_abs"; then
      phantom="${phantom}  - $(basename "$package") -> ${dep_id} ${dep_version} (train '${dep_train}', reached by ProjectReference: the version is stamped at this pack's, and that release never happened)
"
    fi
  done
done
if [ -n "$phantom" ]; then
  echo "error: package(s) would ship a dependency on a package that will never exist:" >&2
  printf '%s' "$phantom" >&2
  echo "       Across trains, depend through a PackageReference on an ALREADY PUBLISHED version." >&2
  echo "       For a helper on no train: put it on this train, or keep its output inside the package." >&2
  exit 1
fi
echo "ok: every dependency is external, co-published, or a published cross-train version"

# --- guard: the SBOM is actually in there --------------------------------------
# Positive proof, not just a green pack: a pack that silently stopped embedding the
# manifest (a GenerateSBOM or Microsoft.Sbom.Targets regression) would otherwise pass
# unnoticed, and the packages would ship without the inventory they promise.
for package in artifacts/*.nupkg; do
  if unzip -l "$package" | grep -q '_manifest/spdx_2.2/manifest.spdx.json'; then
    echo "ok: SBOM present in $package"
  else
    echo "error: SBOM manifest missing from $package" >&2
    exit 1
  fi
done
