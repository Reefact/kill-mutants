# KillMutants

> A modern, opinionated mutation testing tool for .NET, built for xUnit 4.

KillMutants changes your code in small, deliberate ways — turning `>=` into `>`, `&&` into `||` —
and then runs your tests. A test suite that still passes against changed code is a test suite that
was not checking that code. Each change is a *mutant*; a mutant your tests catch is **killed**, one
they miss **survives**.

## Status

Early, but working end to end. Milestone 1 was a deliberately tiny vertical slice — one project
pair, one mutator, one mutant, executed for real — and every milestone since has widened it on that
same foundation: multi-project solutions, coverage-driven test selection, mutants tested in
parallel, console and JSON reports, a `--break-at` quality gate, and a catalogue of eleven mutator
families. It is now packaged as a `dotnet` tool and has been run against its own source. The intent
throughout is a foundation that has been shown to work, rather than a large catalog resting on an
engine that has never run.

## Scope

KillMutants is narrow on purpose, and the narrowness is what keeps it simple.

**Supported:** xUnit 4, modern .NET, C#, SDK-style projects.

**Not supported:** xUnit 2 and earlier, NUnit, MSTest, TUnit, VSTest, .NET Framework, non-SDK
projects, `packages.config`, F#, Visual Basic. No abstraction exists in anticipation of them.

"xUnit 4" is the `xunit.v3` package family at version `4.0.0`; its Microsoft Testing Platform 2
flavour is `xunit.v3.mtp-v2`. Projects built on Microsoft Testing Platform 2 are part of the
ecosystem KillMutants targets, and it runs them — but speaking the MTP protocol is not a goal in
itself, and no MTP coupling is introduced without a concrete need that xUnit 4 cannot already meet.

## Documentation

Every document is maintained in English and in French. Files named `LICENSE` and `README.md` keep
their conventional names and are English only.

| | English | Français |
|---|---|---|
| Architecture — the pipeline, the domain model, the risks | [architecture-en.md](docs/architecture-en.md) | [architecture-fr.md](docs/architecture-fr.md) |
| Study of Stryker.NET — what we learned, and what we deliberately did differently | [stryker-net-en.md](docs/study/stryker-net-en.md) | [stryker-net-fr.md](docs/study/stryker-net-fr.md) |
| Robustness backlog — edge cases inherited from Stryker.NET as specifications and tests | [robustness-backlog-en.md](docs/robustness-backlog-en.md) | [robustness-backlog-fr.md](docs/robustness-backlog-fr.md) |

### Architecture decisions

The few choices that are expensive to reverse. Each is recorded in [docs/decisions](docs/decisions).

| | English | Français |
|---|---|---|
| DEC0001 — Record architecture decisions | [en](docs/decisions/0001-record-architecture-decisions-en.md) | [fr](docs/decisions/0001-record-architecture-decisions-fr.md) |
| DEC0002 — One compilation per mutant | [en](docs/decisions/0002-one-compilation-per-mutant-en.md) | [fr](docs/decisions/0002-one-compilation-per-mutant-fr.md) |
| DEC0003 — Compilation inputs from the csc command line | [en](docs/decisions/0003-compilation-inputs-from-csc-command-line-en.md) | [fr](docs/decisions/0003-compilation-inputs-from-csc-command-line-fr.md) |
| DEC0004 — Run tests by launching the test executable | [en](docs/decisions/0004-run-tests-by-launching-the-test-executable-en.md) | [fr](docs/decisions/0004-run-tests-by-launching-the-test-executable-fr.md) |
| DEC0005 — Verify the baseline before mutating | [en](docs/decisions/0005-verify-the-baseline-before-mutating-en.md) | [fr](docs/decisions/0005-verify-the-baseline-before-mutating-fr.md) |
| DEC0006 — Identify tests by name, not by unique id | [en](docs/decisions/0006-identify-tests-by-name-not-by-unique-id-en.md) | [fr](docs/decisions/0006-identify-tests-by-name-not-by-unique-id-fr.md) |
| DEC0007 — Measure coverage with a type-preserving probe | [en](docs/decisions/0007-measure-coverage-with-a-type-preserving-probe-en.md) | [fr](docs/decisions/0007-measure-coverage-with-a-type-preserving-probe-fr.md) |
| DEC0008 — Never reuse a test host between mutants | [en](docs/decisions/0008-never-reuse-a-test-host-between-mutants-en.md) | [fr](docs/decisions/0008-never-reuse-a-test-host-between-mutants-fr.md) |
| DEC0009 — Exit codes are a public contract | [en](docs/decisions/0009-exit-codes-are-a-public-contract-en.md) | [fr](docs/decisions/0009-exit-codes-are-a-public-contract-fr.md) |
| DEC0010 — A partial run reports findings, not a score | [en](docs/decisions/0010-a-partial-run-reports-findings-not-a-score-en.md) | [fr](docs/decisions/0010-a-partial-run-reports-findings-not-a-score-fr.md) |
| DEC0011 — Widen a partial run's selection when a test file changes | [en](docs/decisions/0011-widen-a-partial-run-selection-when-a-test-file-changes-en.md) | [fr](docs/decisions/0011-widen-a-partial-run-selection-when-a-test-file-changes-fr.md) |

## Installing

Not published to NuGet yet. Pack it and install from the folder you packed into:

```bash
dotnet pack src/KillMutants.Cli -c Release -o ./artifacts

# repository-local, recorded in .config/dotnet-tools.json
dotnet new tool-manifest
dotnet tool install KillMutants --add-source ./artifacts --prerelease
dotnet killmutants

# or machine-wide
dotnet tool install --global KillMutants --add-source ./artifacts --prerelease
killmutants
```

## Using it in CI

```bash
dotnet killmutants --break-at 80 --report-json artifacts/mutation.json
```

Point it at a directory and it finds the xUnit 4 test projects beneath, then everything they
reference. A real repository usually holds some code a run has no business mutating — fixtures,
samples, generated files — so `--exclude` takes it back out. It is repeatable, matched against the
path relative to the directory being scanned, and an excluded project is left out of the run
entirely while an excluded file is still compiled but never mutated:

```bash
dotnet killmutants --exclude "tests/fixtures/*" --exclude "*.Generated.cs"
```

Note that `*` matches `/` as well, so `tests/*` covers everything beneath `tests`.

Not every mutator family earns its keep on every project, and the report says which do. Measured
against this repository: the operator families detect 45% to 55% of what they produce, while
`StringLiteral` and `BooleanLiteral` together account for half the mutants and detect 10% to 15% —
error messages and flags nothing asserts on. Those are true findings, and valuable on projects that
do assert on their messages, so KillMutants reports the split rather than deciding for you:

```bash
dotnet killmutants --without StringLiteral,BooleanLiteral
dotnet killmutants --mutators Comparison,LogicalOperator,Arithmetic
```

Code marked `[ExcludeFromCodeCoverage]` is left alone: the attribute already says this code is not
part of what the tests are expected to cover.

A **test-support library** — builders, fakes, clocks, assertion helpers — is scaffolding rather than
the subject, and mutating it reports findings nobody set out to measure. When it references xUnit it
is recognised on its own, because xUnit refuses to be referenced by a class library and so such a
project is never an `Exe`. When it references nothing in particular, nothing distinguishes it from
the code under test, so it says so itself:

```xml
<PropertyGroup>
  <KillMutantsTestSupport>true</KillMutantsTestSupport>
</PropertyGroup>
```

The project is then left out of the run without hiding what it references: the code under test behind
it is still found and still mutated. On the fixture this was measured against, declaring one support
library moved the score from 25 % to 50 % by removing four mutants nobody had asked about.

A project keeps its habits in `killmutants.json`, beside its code, so a CI job stops retyping flags —
and so the catalogue that produced a score is versioned with the code that was scored:

```jsonc
{
  // Error messages here are not asserted on, so this family only adds noise.
  "without": ["StringLiteral", "BooleanLiteral"],
  "exclude": ["tests/fixtures/*"],
  "breakAt": 80,
  "reportJson": "artifacts/mutation.json"
}
```

Every option has a key — `configuration`, `exclude`, `mutators`, `without`, `parallel`, `coverage`,
`breakAt`, `reportJson` — and anything given on the command line wins, so the file states the habit
and the command line states the exception. A list on the command line replaces the file's rather than
adding to it. Paths in the file are relative to the file. A misspelt key stops the run rather than
being ignored.

Two things in the report exist so a verdict can be argued with. Every mutant carries a `key` derived
from what it is — file, position, family, both texts — so two reports of the same commit can be joined
even though the short `M12`-style numbers shift between runs. And every kill names the tests that
failed, in the form the runner accepts, so a doubted verdict can be settled by hand: put the mutation
in the file at the position given, run those tests, watch them fail.

`--verify-kills <n>` goes further and tests a sample of the kills a second time, on their own. A
verdict that does not survive its own repetition was never a measurement — a flaky or order-dependent
test produces a kill the mutation did not cause — and the report says so rather than quietly picking
a winner. It costs one test run per sampled mutant, so it is off unless asked for.

**A score is only comparable to another score from the same catalogue.** Dropping those two families
on this repository takes the run from 413 mutants in 7.1 minutes to 207 in 4.1, and the score from
28.81% to 43% — not because the tests improved, but because a different question was asked. Pick a
catalogue for a CI job and keep it: `--break-at` compares against whatever families actually ran, and
the JSON report lists them under `byMutator` so a consumer can tell which.

The score is `detected / (detected + undetected)`. A mutant the tests caught is detected, and so is
one that hung the suite until the timeout — the tests noticed it. A mutant that survived is
undetected, and **so is one no test reaches**: the suite would not have noticed the change, and
skipping its test run is an optimisation rather than a pardon. Only a mutant KillMutants could not
build is left out, because there the suite was never asked. Uncovered code therefore lowers the
score, which is the point.

| exit code | meaning |
|---|---|
| 0 | Ran, and met the threshold if one was given |
| 1 | Ran, but the score is below `--break-at` |
| 2 | Could not run; the reason is on standard error |
| 64 | The command line was not understood |

Progress is written to standard error and the report to standard output, so redirecting the report
does not drag the progress line along with it.

## Licence

[PolyForm Internal Use License 1.0.0](LICENSE).
