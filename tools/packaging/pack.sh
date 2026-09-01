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

projects="$(projects_of "$train")"
if [ -z "$projects" ]; then
  echo "error: no project declares <ReleaseTrain>${train}</ReleaseTrain>; there is nothing to publish on this train." >&2
  exit 1
fi

root="$(cd "$(dirname "$0")/../.." && pwd)"

echo "Packing the '${train}' train at ${version}:"
for project in $projects; do
  echo "  ${project}"
done

# --- guard: no cross-train project reference ----------------------------------
# The trains version independently, so a package on one train may only depend on
# another train through a PUBLISHED version. `dotnet pack` turns a ProjectReference
# into a dependency stamped at the version being packed — so a ProjectReference
# across trains would declare a dependency on a version of the other train that was
# never published, making the package unresolvable (NU1102) for every consumer, on
# an immutable artifact.
#
# This is the failure mode the cli train invites: the tool naturally wants to
# reference the engine's project, and doing so would publish a tool depending on an
# engine version that does not exist. Across trains it must be a PackageReference.
#
# Checked on the PROJECT FILES rather than on the produced nuspec, because that is
# where the answer is exact: a nuspec cannot distinguish a ProjectReference-derived
# dependency from a legitimate PackageReference that happens to carry the same
# version. A reference to a project declaring NO train is left alone: that is the
# ordinary way an analyzer or a private helper is bundled into a package.
violations=''
# Intentionally unquoted: the project list is newline-separated with no spaces in paths.
for project in $projects; do
  project_dir="$(dirname "$project")"
  # Both XML quoting forms. MSBuild accepts Include='...' as readily as Include="...", and a
  # double-quote-only expression finds nothing in a project written with apostrophes — so the
  # guard below would report success over exactly the reference it exists to catch.
  references="$(sed -n \
    -e 's|.*<ProjectReference[^>]*Include="\([^"]*\)".*|\1|p' \
    -e "s|.*<ProjectReference[^>]*Include='\([^']*\)'.*|\1|p" \
    "$project")"
  for reference in $references; do
    # Project files carry Windows separators; translate, then resolve against the
    # referring project's directory so the '..' segments collapse.
    # shellcheck disable=SC1003  # '\\' is tr's escape for a literal backslash, not a mis-escaped quote
    reference_path="$(printf '%s' "$reference" | tr '\\' '/')"
    resolved="$(cd "$root/$project_dir" && realpath -m "$reference_path")"
    # A reference that does not resolve to a file is a broken project file. That is
    # the build's failure to report, with a better message than this script could
    # give, so skip rather than duplicate it.
    [ -f "$resolved" ] || continue
    referenced_train="$(sed -n 's|.*<ReleaseTrain>[[:space:]]*\([^<[:space:]]*\)[[:space:]]*</ReleaseTrain>.*|\1|p' "$resolved" | head -n1)"
    [ -n "$referenced_train" ] || continue          # on no train: bundled, not depended upon
    [ "$referenced_train" = "$train" ] && continue  # same train, co-published at this very version
    violations="${violations}  - ${project} -> ${resolved#"$root"/} (train '${referenced_train}')
"
  done
done
if [ -n "$violations" ]; then
  echo "error: cross-train ProjectReference(s) found while packing the '${train}' train:" >&2
  printf '%s' "$violations" >&2
  echo "       A package may only depend on another train through a published PackageReference." >&2
  exit 1
fi
echo "ok: no cross-train ProjectReference on the '${train}' train"

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
for project in $projects; do
  dotnet pack "$project" -c Release -p:Version="$version" -p:GenerateSBOM=true -o artifacts
done

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
