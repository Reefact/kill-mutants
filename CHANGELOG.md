# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each release train keeps its entries here until its projects carry changelogs of
their own; see `tools/trains.sh` for the trains and `RELEASING.md` for the procedure.

`tools/packaging/check-changelog.sh` refuses to publish a version this file does not
document with a **dated** heading, so an entry left under "Unreleased" fails the
release rather than shipping undocumented.

## [Unreleased]

### Changed

- `Statement` also removes a `yield return`, unless it is the only one of its iterator, whose
  removal would not compile. Removing one skips an element and leaves the iterator well-formed, so
  it needs none of the flow analysis that keeps `return`, `throw`, `break` and `yield break` out
  (DEC0020). Found on `first-class-errors`, where a tokenizer in a security analyzer could drop the
  token before a separator or a camel-case hump without a test noticing, and no family had a
  mutant on either line. See #62.
- A part of the run that cannot be measured is set aside with its reason, and the run carries on:
  a project MSBuild cannot read, a test project that does not build, a compilation that cannot be
  reconstructed, a red baseline, a failed coverage pass. Each was a stop; now the report names them
  under "Set aside" (and `setAside` in the JSON), the score covers the part measured and says so -
  or is withheld when a project could not be read - no gate passes, and the exit code is `2`. In
  the JSON, `complete` is then `false` and `mutantsSelected` counts the mutants left unjudged. A
  `--resume` run of the series completes what was set aside, including a target whose mutants were
  never generated. See DEC0022.

### Added

- An opt-in `Linq` family, run with `--with Linq` or `--mutators Linq`, swaps a call for its
  counterpart in eleven pairs - `Any`/`All`, `Min`/`Max`, `MinBy`/`MaxBy`, `OrderBy`/
  `OrderByDescending`, `ThenBy`/`ThenByDescending`, `Order`/`OrderDescending`, `Skip`/`Take`,
  `SkipWhile`/`TakeWhile`, `SkipLast`/`TakeLast`, `Union`/`Intersect`, `Concat`/`Except` - when the
  counterpart binds to a twin of the method called: same containing type, same return type,
  returned the same way. The rule names no class, so `ImmutableArray`'s `Any` is reached as
  `Enumerable`'s is, and a method of your own that merely shares a name is left alone. Two
  `Any` → `All` gaps confirmed on `first-class-errors` motivated it. See DEC0024, #63.
- `--ignore-untracked <pattern>` (and `ignoreUntracked` in `killmutants.json`) names the untracked
  files a `--resume` series does not count when it reads its tree, such as the `.received` files a
  snapshot test writes beside its source. Each file ignored is named on standard error and recorded
  per run in the report's `series.runs` (`ignoreUntracked`, `ignoredUntracked`). A pattern that could
  cover a source or build file is refused before the run, and a run whose compiler reads an ignored
  file is refused once the build has named its inputs. See DEC0025.
- A series refused for a dirty tree names the untracked files that appeared since it began, as
  likely written by its own run, and says whether tracked files differ too (#66).
- A run that finds no xUnit test project names the projects that reference xUnit but were never
  restored, and asks for `dotnet restore`, before what it set aside: a test project that leaves its
  output type to a package such as `Microsoft.NET.Test.Sdk` is only an executable once restored, and
  the refusal used to point at an unrelated project it had set aside (#66).

- `--target-framework <tfm>` (and `targetFramework` in `killmutants.json`) names the one framework the
  test projects run on. A test project targeting several frameworks was refused outright; it is
  now refused only without the option, and the message lists what it offers. See DEC0021.
- Release pipeline: tag-triggered publish to NuGet with OIDC trusted publishing,
  SLSA build provenance attestation, embedded SPDX SBOM, and a side-effect-free
  rehearsal (`release-dryrun`) on every pull request.
