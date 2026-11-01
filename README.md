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
parallel, console and JSON reports, a `--break-at` quality gate, and a catalogue of fourteen mutator
families, with a fifteenth to opt in to. It is now packaged as a `dotnet` tool and has been run against its own source. The intent
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
| DEC0012 — A partial run withholds its verdict over an incomplete earlier state | [en](docs/decisions/0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-en.md) | [fr](docs/decisions/0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-fr.md) |
| DEC0013 — A stopped run reports what it judged and passes no gate | [en](docs/decisions/0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md) | [fr](docs/decisions/0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md) |
| DEC0014 — Each verdict is appended to a log beside the report as soon as it is final | [en](docs/decisions/0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md) | [fr](docs/decisions/0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md) |
| DEC0015 — A resumed run inherits the verdicts its series reached on the same code | [en](docs/decisions/0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md) | [fr](docs/decisions/0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md) |
| DEC0016 — The run writes its plan beside the report, as a file of its own | [en](docs/decisions/0016-the-run-writes-its-plan-beside-the-report-en.md) | [fr](docs/decisions/0016-the-run-writes-its-plan-beside-the-report-fr.md) |
| DEC0017 — A ternary's condition is forced each way, still evaluated, rather than its branches swapped | [en](docs/decisions/0017-a-ternary-is-forced-each-way-rather-than-swapped-en.md) | [fr](docs/decisions/0017-a-ternary-is-forced-each-way-rather-than-swapped-fr.md) |
| DEC0018 — The patterns of an `is` are mutated by a family of their own, as the catalogue's operators are | [en](docs/decisions/0018-patterns-are-mutated-by-a-family-of-their-own-en.md) | [fr](docs/decisions/0018-patterns-are-mutated-by-a-family-of-their-own-fr.md) |
| DEC0019 — Numeric literals are an opt-in family, moved one step each way, that reaches the constants its project reads | [en](docs/decisions/0019-numeric-literals-are-an-opt-in-family-that-reaches-the-constants-its-project-reads-en.md) | [fr](docs/decisions/0019-numeric-literals-are-an-opt-in-family-that-reaches-the-constants-its-project-reads-fr.md) |
| DEC0020 — A call statement is removed by a default family, and its coverage is measured by a probe placed before it | [en](docs/decisions/0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-en.md) | [fr](docs/decisions/0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-fr.md) |
| DEC0021 — The developer chooses the framework the test projects run on, and an ambiguous run is refused without that choice | [en](docs/decisions/0021-the-developer-chooses-the-framework-the-test-projects-run-on-en.md) | [fr](docs/decisions/0021-the-developer-chooses-the-framework-the-test-projects-run-on-fr.md) |
| DEC0022 — A run carries on past what fails, sets it aside with its reason, and reports what it measured and what it could not | [en](docs/decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-en.md) | [fr](docs/decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-fr.md) |
| DEC0023 — The console progress mode is chosen explicitly, with `log` as the default and no detection of the terminal | [en](docs/decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-en.md) | [fr](docs/decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-fr.md) |
| DEC0024 — An opt-in `Linq` family swaps a method for its counterpart, when the counterpart is a twin on the same type | [en](docs/decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-en.md) | [fr](docs/decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-fr.md) |
| DEC0025 — A series names the untracked files that do not make its tree dirty, and a pattern that could cover code is refused | [en](docs/decisions/0025-a-series-names-the-untracked-files-that-do-not-make-its-tree-dirty-en.md) | [fr](docs/decisions/0025-a-series-names-the-untracked-files-that-do-not-make-its-tree-dirty-fr.md) |
| DEC0026 — A project names the calls it silences, and the report counts what it chose not to hear | [en](docs/decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-en.md) | [fr](docs/decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-fr.md) |

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

A run that outgrows its CI budget should not take its measurements down with it. `--stop-after`
gives the run a deadline of its own; set it below the job's timeout, with room for the mutants still
in flight and for writing the report:

```bash
dotnet killmutants --stop-after 80m --report-json artifacts/mutation.json
```

When the deadline comes, the run stops testing and reports the verdicts it reached. Such a report
says it stopped, prints no score — the mutants it judged are the ones it reached first, not a sample
— and passes no gate: it exits `1` when `--break-at` or `--since` asked for one, and `0` otherwise,
with a line on standard error either way. Ctrl-C, SIGINT and SIGTERM stop it the same way, and a
second Ctrl-C ends it at once. The deadline is the one that works on GitHub Actions, where a job's
own timeout signals the step's shell rather than the tool and kills everything seconds later
([DEC0013](docs/decisions/0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md)).

A test project may target several frameworks, and a score describes one build of the code, so a run
has to be told which one. Without it the run stops and lists what each test project offers; with it,
every test project runs on that framework and the libraries follow the one NuGet resolves for it:

```bash
dotnet killmutants --target-framework net10.0
```

One framework per run — run it again for another. A test project that does not offer the framework
stops the run too, rather than dropping out of the report unseen; `--exclude` sets it aside on
purpose. The same choice can live in `killmutants.json` as `targetFramework`
([DEC0021](docs/decisions/0021-the-developer-chooses-the-framework-the-test-projects-run-on-en.md)).

Every verdict also lands, as soon as it is final, in a JSON Lines log beside the report — the same
path ending in `.jsonl`, here `artifacts/mutation.jsonl` — one line per mutant, in the shape of the
report's `mutants` entries. It is there for the run that never gets to write its report: a job
cancelled by a newer push, a runner that was lost. A report an earlier run left at that path is
cleared when the run starts, so a log with no report beside it is always a run that did not finish.
A timeout is logged only once it has been confirmed on its own, so no line is ever contradicted by a
later one
([DEC0014](docs/decisions/0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md)).

Every run that writes a report also writes a plan beside it — the same path ending in `.plan.json`,
here `artifacts/mutation.plan.json` — that says what code and settings it measured and lists every
mutant it selected, so that plan minus log is what remains after a killed run, readable without the
tool. It is written before anything is discovered, with the mutant list still `null`, and rewritten
whole once the mutants are selected, so a run stopped during the build leaves one too. A report path
ending in `.jsonl` or `.plan.json` is refused, because those names are the log's and the plan's
([DEC0016](docs/decisions/0016-the-run-writes-its-plan-beside-the-report-en.md)).

A project that does not fit in one budget is finished over several runs. `--resume <id>` makes the
run belong to a *series* named by the caller — the commit hash, typically:

```bash
dotnet killmutants --resume "$GITHUB_SHA" --stop-after 80m --report-json artifacts/mutation.json
```

The first run of a series starts it. A later run given the same name, on the same commit with a
clean tree, pointed at the same directory of it, with the same settings and the same tool, inherits
every verdict the log holds — except
timeouts, which are only final under the budget that confirmed them, and kills no test is named for,
which a stop can manufacture — and judges only the rest; a project with nothing left gets no sandbox,
no baseline and no coverage pass, and runs no test at all. The run that reaches the last verdict
reports the whole series, score included; until then each run is a stopped run, and exits as one. A
series of that name on other code, or with other settings, is refused with the difference named and
nothing touched. Another name, or no name, over the same report path starts afresh: the plan is
replaced and the log emptied, so a series shares its report path with nothing else. What travels
between jobs is the plan and the log — an artifact, a cache keyed on the commit — and the checkout
has to stay on the series' commit until the series is done
([DEC0015](docs/decisions/0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md)).

A series resumed **in the directory it was interrupted in** — on a workstation, typically — can find
files its own run wrote: a snapshot test writes a `.received` file beside its source when a mutant
changes its output. The refusal names the untracked files that appeared since the series began, and
the project can name them for good with `--ignore-untracked` (`ignoreUntracked` in
`killmutants.json`), which only a series takes:

```bash
dotnet killmutants --resume "$SHA" --report-json out/km.json \
  --ignore-untracked '*.received.html' --ignore-untracked '*.received.json'
```

Only untracked files under the directory are ignored, matched as `--exclude` matches; an edited
tracked file still makes the tree dirty. Every file ignored is named on standard error and recorded
in the report, against the run that ignored it. A pattern that could cover a file that changes what
is built is refused before anything runs, case ignored: one made only of wildcards, and any that can
match the same path as `*.cs`, `*.csproj`, `*.props`, `*.targets`, `*.globalconfig`, `*.resx`,
`.editorconfig`, `global.json` or `NuGet.config` — so `*.received.*` is refused, since it accepts
`x.received.cs`, and the extensions are named instead. A run whose compiler reads an ignored file is
refused too, once the build has named its inputs. Neither check sees a file a test reads at run time
([DEC0025](docs/decisions/0025-a-series-names-the-untracked-files-that-do-not-make-its-tree-dirty-en.md)).

## Judging a change rather than a codebase

A full sweep of a real repository takes minutes; a diff takes seconds. `--since` runs only what a
change touched, which is what makes mutation testing usable on a pull request rather than nightly:

```bash
dotnet killmutants --since origin/main
```

The change is measured from the merge base of that revision and `HEAD` to the **working tree**, since
the working tree is what gets built. On a clean checkout — every CI job — that is the same thing as
the commit; on a laptop with uncommitted work it is not, and the report says which it was.

Changed production code is selected precisely, file by file. Anything touching an *existing* file in
a test project widens instead, to every mutable project that test project exercises: what a change to
a test removes is a coverage edge, and there is no asking `HEAD` about an edge that is no longer
there. That relation is read at both revisions, so removing a project reference in the very change
being judged does not delete the answer along with the question. A C# file the change *adds* to a
test project widens nothing — a new test cannot have removed an edge that predates it — while an
added fixture, case list or settings file widens like any other change, because that argument was
never about those.

**A widened run says so.** One edited test can put a whole solution in scope, so the report names
every project it took whole and the changes that made it, and counts how many of the undetected
mutants sit in files the change did not touch — the first thing a reviewer wants to know about a red
gate. Those mutants still count, and still fail the verdict: a change to a test can stop a mutant
being killed without touching the file it sits in. In `--report-json` they are `widened` and
`undetectedFromWidening`.

**A partial run prints no mutation score, and that is deliberate.** Its population is the change
itself, chosen against a base revision that differs every run, so a percentage over it answers
nothing that spans two runs. It prints the status counts, the findings by name, and a binary verdict:
the run fails if the selected scope holds an undetected mutant — survived *or* uncovered, since code
a change adds that nothing tests at all is the second and not the first. A threshold is therefore
refused with `--since`; pass `--break-at none` to clear one your `killmutants.json` sets.

A partial run also fails when the change leaves **no test reaching a project at all**. That project
has no mutants to report — nothing exercises it, so no suite could judge one — which is exactly how
deleting a component's last test used to read as a clean pass. It is named instead. A project you
excluded, or one that declares itself test support, is not this: those are deliberate.
[DEC0010](docs/decisions/0010-a-partial-run-reports-findings-not-a-score-en.md) argues what such a
run may print and [DEC0011](docs/decisions/0011-widen-a-partial-run-selection-when-a-test-file-changes-en.md)
the selection rule; both record what the implementation deliberately does not do.

**A partial run withholds its verdict entirely when it could not read the earlier state in full.** A
component whose objects are not in your clone — a submodule before `git submodule update --init` —
cannot be reconstructed, and what it would have contributed cannot be recovered from what remains: a
project outside it can import a build file from inside it, and MSBuild skips that import silently, so
the references it would have added are gone with nothing left to detect them by. The run still
happens and still lists the mutants it ran; it prints `No verdict` rather than a passing one, names
the components, and exits non-zero. Fetch them and run again, or run without `--since`.
[DEC0012](docs/decisions/0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-en.md)
records why abstaining beats refusing the run or passing with a warning attached.

A change to `killmutants.json` is refused: that file decides what a run measures, and a partial run
cannot judge a change to its own configuration. Run without `--since` for that one.

`--since` needs git, and needs the base revision to be in the clone: a shallow CI checkout often is
not enough, and the run says so rather than silently comparing against whatever it can reach.

Not every mutator family earns its keep on every project, and the report says which do. Measured
against this repository: the operator families detect 45% to 55% of what they produce, while
`StringLiteral` and `BooleanLiteral` together account for half the mutants and detect 10% to 15% —
error messages and flags nothing asserts on. Those are true findings, and valuable on projects that
do assert on their messages, so KillMutants reports the split rather than deciding for you:

```bash
dotnet killmutants --without StringLiteral,BooleanLiteral
dotnet killmutants --mutators Comparison,LogicalOperator,Arithmetic
```

A family is a blunt instrument for a finer choice. Measured on `Reefact/slugger`, `Statement` left
144 survivors, 116 of them argument guards - `ArgumentNullException.ThrowIfNull(x)` and its
siblings, covered by tests that never pass `null` - burying 9 real gaps. A project says which
**calls** it does not want to hear about with `--ignore-calls` (`ignoreCalls` in
`killmutants.json`):

```bash
dotnet killmutants --ignore-calls 'System.ArgumentNullException.ThrowIfNull' \
  --ignore-calls 'System.ArgumentException.ThrowIf*' --ignore-calls '*.ConfigureAwait'
```

A pattern is matched against the fully qualified name of the method the call **binds to**, with `*`
and `?` as `--exclude` has them and case as C# has it, so a method of your own that merely shares a
name with `ThrowIfNull` is not caught; a constructor is `System.ArgumentException.ctor`. A bare
`ThrowIfNull` recognises nothing, and the report says so. A recognised call silences its statement
and its arguments - `ConfigureAwait(false)`'s literal, an exception's message - and nothing beyond:
neither what the call is made on, which it passes along, nor the body of a lambda or a local
function handed to it, which is code of its own. A silenced mutant is
still generated, never tested, and reported as **ignored**: outside the score, counted per family,
counted per rule with a rule that silenced nothing named, and the count stands beside the score
itself. The choice is the project's, it is versioned, and the report shows it rather than hiding it
([DEC0026](docs/decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-en.md)).
A series refuses to continue under another list, as it refuses other exclusions.

Two families run only when they are asked for. `NumericLiteral` moves each integer a constant, a field
or a property is initialized with one step each way — `MinimumNouns = 100` becomes `101`, then `99` —
which asks whether the tests check a threshold *at* its boundary or only somewhere around it. No
operator is involved there, so no other family can ask it. Literals are everywhere and most are not
thresholds, so it is off by default: `--with` adds it to the others, and `--mutators` names it alone.

```bash
dotnet killmutants --with NumericLiteral
dotnet killmutants --mutators NumericLiteral
```

It reaches a `const` only when the project itself reads it. A test assembly keeps the value a
constant had when it was compiled, so a constant only the tests read would survive whatever they
assert; a constant the project reads changes there with the mutant, and a test can notice. Mutants in
a constant are tested against the whole suite, since there is nothing to measure coverage on
([DEC0019](docs/decisions/0019-numeric-literals-are-an-opt-in-family-that-reaches-the-constants-its-project-reads-en.md)).

`Linq` swaps a call for its counterpart - `Any` for `All`, `Min` for `Max`, `OrderBy` for
`OrderByDescending`, `Skip` for `Take`, `Union` for `Intersect`, eleven pairs in all - which asks
whether a test exercises a quantifier, an order or a slice on both sides, or on one only. A call is
swapped only when the counterpart binds to a twin of the method called: declared on the same type,
returning the same type the same way. So the rule names no class - `ImmutableArray`'s `Any` is
reached as `Enumerable`'s is - and leaves alone a method of your own that merely shares a name. It
is off by default until it has been measured (DEC0024):

```bash
dotnet killmutants --with Linq
dotnet killmutants --mutators Linq
```

Code marked `[ExcludeFromCodeCoverage]` is left alone: the attribute already says this code is not
part of what the tests are expected to cover.

A **test-support library** — builders, fakes, clocks, assertion helpers — is scaffolding rather than
the subject, and mutating it reports findings nobody set out to measure. Nothing structural tells one
apart from the code under test: referencing `xunit.v3.assert` no longer gets such a library
*mistaken for a test project*, but it stays an ordinary mutable project all the same. So it says so
itself:

```xml
<PropertyGroup>
  <KillMutantsTestSupport>true</KillMutantsTestSupport>
</PropertyGroup>
```

The project is then left out of the run without hiding what it references: the code under test behind
it is still found and still mutated. The declaration outranks everything else the project looks like,
so a helper that happens to be a runnable test application is not launched as a suite either. On the
fixture this was measured against, declaring one support library took the run from four mutants to
two — dropping the two nobody had asked about — and the score from 25 % to 50 %.

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

Every option that states a habit has a key — `configuration`, `exclude`, `mutators`, `with`,
`without`, `parallel`, `coverage`, `breakAt`, `verifyKills`, `reportJson`, `stopAfter`, `ignoreCalls` — and anything given on
the command line wins, so the file states the habit and the command line states the exception.
`--since` and `--resume` say what one run is about, and have none. A list on the command line replaces the file's rather than
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
| 0 | Ran, and the gate you asked for passed — or you asked for none |
| 1 | Ran, but the gate did not pass; standard error says which case |
| 2 | Could not run, or ran but set part of its scope aside; the report or standard error says why |
| 64 | The command line was not understood |

`1` is named for the gate rather than for one of its causes, because it has more than one: a score
below `--break-at`, and a score that is **undefined** because no mutant could be tested — a run that
demonstrated nothing cannot be shown to have met a threshold, and reporting success would let a
misconfigured job stay green for ever. A run stopped before the end joins it for the same reason,
when it was given a gate: it has not judged everything the gate is about. A build script branches on *findings* either way; standard
error says which case it was. The mapping is fixed
([DEC0009](docs/decisions/0009-exit-codes-are-a-public-contract-en.md)): a code is never renumbered,
and a new cause of an outcome a code already names joins it rather than taking a code of its own.

**A part that cannot be measured is set aside, and the run carries on**
([DEC0022](docs/decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-en.md)).
A project MSBuild cannot read, a test project that does not build, a project whose compilation
cannot be reconstructed, a suite that does not pass against unmutated code, a coverage pass that
fails: each is named at the top of the report under *Set aside*, with the reason, and the rest of
the run is measured as usual. Such a run passes no gate and exits `2`. Its score covers the part
measured and says so; when a project could not be read at all, it may have been the only suite
reaching some code, so no score is printed. A mutant that cannot be judged is not set aside: it is
an outcome inside the scope, counted as untestable. A run still stops when nothing can advance - no
test project at all, a command line or configuration it cannot act on.

Progress is written to standard error and the report to standard output, so redirecting the report
does not drag the progress along with it. What standard error shows is chosen with `--progress`
(`progress` in `killmutants.json`); nothing about the terminal is detected
([DEC0023](docs/decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-en.md)):

| Mode | For | What it shows |
|---|---|---|
| `log`, the default | a CI log, a file, a pipe, and anyone who asked for nothing | dated lines: each phase once, then its count and the mutant in progress at most once a minute |
| `live` | a person at a terminal | one line rewritten in place: the phase, the count and the mutant in progress, with a bar, the percentage, the elapsed and remaining time and the verdicts so far as the width allows - those are left out first on a narrow terminal |
| `none` | a tool that reads the JSON report | nothing but the parts set aside |

Two events are written the moment they happen rather than at the next count: a part set aside, with
the first line of its reason, in every mode; and, except under `none`, a mutant still running after
`--slow` (30 seconds by default), once, with the budget it was given. These lines are for people and
may change between versions; a tool reads `--report-json`.

## Licence

[PolyForm Internal Use License 1.0.0](LICENSE).
