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

- A declaration or an assignment inside a lambda, an anonymous method or a local function no
  longer refuses the expression around it (RB-016, RB-030): the variable is scoped to the function,
  so the call around it is mutated and probed without orphaning anything, while the expression
  inside that declares it is refused as before. Found on `enum-member-name-binding`, where a sort
  key reading an `out` variable had no mutant from any family. See #78.
- An argument that is a capacity - `new StringBuilder(n * 4)`, `new List<T>(n + 1)`,
  `EnsureCapacity(n)`, on a type under `System` - is no longer a mutation site, as a constant is
  not: a capacity changes when the buffer grows, never what it holds, so every mutant of it survived
  for a reason that said nothing about the tests. Recognised by the parameter's name, which the BCL
  reserves for it; a `capacity` of the user's own is still mutated. Found on
  `enum-member-name-binding`, where it was the one survivor read as equivalent in both KillMutants'
  and Stryker.NET's reports. See RB-033, #76.
- `NullCoalescing` asks whether the left of `a ?? b` is ever non-null where it could not before:
  when the swap `b ?? a` is refused - the left is a call, a getter that runs code, a fallback that
  cannot stand on the left - the fallback is forced with the left still evaluated,
  `(a is null ? b : b)`, in the manner DEC0017 settled for a ternary. Keeping only `b`, as
  Stryker.NET does, is still not made, since it drops the left's side effects. Found on
  `enum-member-name-binding`, where `Nullable.GetUnderlyingType(enumType) ?? enumType` reached no
  test with a nullable enum and no mutant could say so. See RB-034, #77.
- `Linq` runs by default. It entered the catalogue opt-in until it had been measured (DEC0024); on
  `enum-member-name-binding` it made five mutants on three hundred, every one compiled, four killed
  and the one survivor a low-value finding, which is `Statement`'s profile. `--with Linq` is no
  longer needed and still works; `--without Linq` (or `"without": ["Linq"]`) leaves it out. A series
  started before this change does not continue across it, since its plan names another catalogue.
  See DEC0027, #82.
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

- `--ignore-calls <pattern>` (and `ignoreCalls` in `killmutants.json`) names the calls a project does
  not want to hear about, by the fully qualified name of the method or constructor the call binds to,
  with `*` as in `--exclude`. A mutant of such a call's statement or arguments is generated, never
  tested, and reported as `Ignored`: outside the score, counted per family and per rule - a rule
  that silenced nothing is named - and beside the score itself; `totals.ignored`, `ignoredCalls` and
  each mutant's `ignoredBy` in the JSON. The body of a lambda or local function handed to the call is
  not silenced. A series refuses to continue under another list. See DEC0026 (#42).

- An opt-in `Linq` family, run with `--with Linq` or `--mutators Linq`, swaps a call for its
  counterpart in eleven pairs - `Any`/`All`, `Min`/`Max`, `MinBy`/`MaxBy`, `OrderBy`/
  `OrderByDescending`, `ThenBy`/`ThenByDescending`, `Order`/`OrderDescending`, `Skip`/`Take`,
  `SkipWhile`/`TakeWhile`, `SkipLast`/`TakeLast`, `Union`/`Intersect`, `Concat`/`Except` - when the
  counterpart binds to a twin of the method called: same containing type, same return type,
  returned the same way. The rule names no class, so `ImmutableArray`'s `Any` is reached as
  `Enumerable`'s is, and a method of your own that merely shares a name is left alone. Two
  `Any` → `All` gaps confirmed on `first-class-errors` motivated it. See DEC0024, #63.
- `--progress <mode>` (and `progress` in `killmutants.json`) chooses what standard error shows while
  the run runs: `log`, the default, dated lines with the mutant in progress once a minute; `live`,
  one line rewritten in place with the mutant in progress, and the bar, the time left and the
  verdicts so far as the terminal's width allows; `none`, only the parts set aside. Nothing about the terminal is detected any more, where the
  form used to follow whether standard error was redirected. A part set aside is written the moment
  it happens, and `--slow <time>` (30 seconds by default) names a mutant still running after that
  long, once. The lines are not an interface: tools read the JSON report. See DEC0023 (#65).

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
