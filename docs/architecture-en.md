# KillMutants — architecture

> A modern, opinionated mutation testing tool for .NET, built for xUnit 4.

## 1. Constraints

These are commitments, not defaults. They are what lets the design stay small.

**Supported:** xUnit 4, modern .NET (net10.0), C#, SDK-style projects.

**Not supported, and no abstraction exists in anticipation of them:** xUnit 2 and earlier, NUnit,
MSTest, TUnit, VSTest, .NET Framework, non-SDK project formats, `packages.config`, F#, Visual Basic.

"xUnit 4" is the `xunit.v3` package family at version `4.0.0` (published 2026-08-15). There is no
`xunit.v4` package id. The Microsoft Testing Platform 2 flavour is `xunit.v3.mtp-v2`.

**Where Microsoft Testing Platform sits.** MTP 2 is part of the ecosystem we target, not a constraint
we design around. xUnit 4 projects may or may not run on it, and KillMutants handles both. What the
tool needs is the simplest, most reliable and fastest xUnit 4 execution path for the job at hand;
today that is xUnit's own runner, which also supplies `-stopOnFail`, `-list tests /json` and
`-id <uid>`. Direct MTP coupling — a JSON-RPC client, say — is introduced only when a concrete need
of KillMutants justifies it *and* xUnit 4 cannot already meet it.

KillMutants takes **no dependency on any xUnit or MTP package**. It launches the test project's
executable as a child process. The coupling is knowledge of a command-line contract, not a
reference — which is the strongest form of "localized dependency" available here.

## 2. Verified ground truth

Every number below was measured on this platform (SDK 10.0.111, runtime 10.0.11), not recalled.

| Fact | Value |
|---|---|
| Roslyn emit, reusing the compilation | **6 ms / mutant** |
| `dotnet build` of the same project | ~1400 ms |
| Test executable run (2 tests) | ~600 ms |
| Mutant phase, 60 mutants, 4 cores — 1 / 2 / 4 workers | 1.0x / ~2.1x / ~3.2x |
| 60 mutants, slow suite — whole suite vs coverage-selected | 29.3 s vs 22.6 s |
| Test host launch vs the testing it does | ~0.5 s vs ~0.12 s |
| `dotnet test --no-build` | ~1500 ms |
| Test exe exit code — pass / fail | 0 / **1** |
| `dotnet test` exit code — pass / fail / no tests | 0 / 2 / 8 |

The decisive ratio is **compile : test ≈ 1 : 100**. Compilation is not the bottleneck.

## 3. The pipeline

```
  discover  ->  analyse  ->  generate  ->  [ per mutant: apply -> compile -> inject -> run -> classify ]  ->  report
```

Concretely, one run is:

1. **Discover** the project under test and the test project that exercises it.
2. **Analyse**: ask MSBuild for the exact `csc` command line, turn it into a `CSharpCompilation`.
3. **Verify the baseline**: emit the *unmutated* compilation, inject it, run the tests, require
   green. This is not optional — see [DEC0005](decisions/0005-verify-the-baseline-before-mutating-en.md).
   It is required per target: a target whose baseline is not green is set aside with the reason,
   and the others are measured — see [DEC0022](decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-en.md).
4. **Generate** mutants by walking the syntax trees with the mutator catalog.
5. For each mutant: replace the syntax tree, emit, write the assembly into the test project's
   output directory, run the test executable, classify the outcome, restore the original.
6. **Report** the counts and the mutation score.

## 4. How the conceptual concerns map onto code

The concerns below are kept distinct as namespaces and types. Most of them share one assembly: for
milestone 1, thirteen projects for a few hundred lines would have been the premature structure this
project set out to avoid, and the namespace boundaries sit where assembly boundaries would go.

Three assemblies do exist, and the split is load-bearing rather than cosmetic. `KillMutants.Core`
declares `IChangeSource` and `ICodeSnapshot` — what a partial run needs from whatever knows what
changed — and implements neither; it names no implementation anywhere, so it cannot reach one even
by accident. `KillMutants.Git` implements them and references the core. `KillMutants.Platform` holds
what both need from the operating system — running a process under a time budget, naming and
creating the directory a run works in, deleting it without ever failing a run over it — so that
neither has to name the other to share it. It is also where the one dependency beyond Roslyn lives:
Slugger, which draws those names from the kill-mutants theme embedded beside it. The CLI is the
only place that knows git is what answers today, which is where composition belongs. See DEC0011 for
what the contract is for.

| Concern | Namespace | Notes |
|---|---|---|
| Project discovery | `KillMutants.Projects` | Locates the project under test and its test project |
| Code analysis | `KillMutants.Analysis` | `csc` command line -> `CSharpCompilation` |
| Scope filtering | `KillMutants.Filtering` | What a run leaves alone (`--exclude`) |
| Mutation generation | `KillMutants.Mutations` | Walks trees, produces candidates |
| Mutator catalog | `KillMutants.Mutations.Mutators` | `IMutator` and its implementations |
| Mutant representation | `KillMutants.Mutations` | `Mutant`, `MutantId`, `MutantStatus`, `SourceLocation` |
| Instrumentation | *(collapsed)* | See below |
| Compilation | `KillMutants.Compilation` | Emits a mutated assembly |
| Test discovery | `KillMutants.Testing` | `-list tests /json`, by name (DEC0006) |
| Test execution | `KillMutants.Testing` | `ITestRunner`, `TestRunOutcome` |
| xUnit 4 / MTP 2 specifics | `KillMutants.Testing.XUnit` | The only place that knows the runner's CLI |
| Test-to-mutant mapping | `KillMutants.Coverage` | A type-preserving probe, or a mark placed before a statement, one run per test (DEC0007, DEC0020) |
| Orchestration | `KillMutants.Execution` | The short, linear phase list |
| Results | `KillMutants.Reporting` | `MutationTestReport`, console and JSON writers, progress |
| CLI | `KillMutants.Cli` | `dotnet killmutants`, thresholds and exit codes (DEC0009) |

**The catalogue.** Sixteen families, each a separate `IMutator`, each with its own tests and each
exercised end to end against a real fixture project. Fourteen run by default; `NumericLiteral` and
`Linq` run only when `--with` adds them or `--mutators` names them (DEC0019, DEC0024).

| Family | Rewrites | Into |
|---|---|---|
| `Comparison` | `>=` `>` `<=` `<` | the boundary shift and the negation |
| `Comparison` | `==` `!=` | the negation only — there is no boundary to shift |
| `LogicalOperator` | `&&` `\|\|` | each other |
| `Arithmetic` | `+` `-` `*` `/` `%` | its counterpart |
| `Bitwise` | `&` `\|` `^` `<<` `>>` | its counterpart |
| `Assignment` | `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `<<=` `>>=` | its counterpart |
| `Increment` | `++` `--` | each other, prefix and postfix alike |
| `Conditional` | `c ? a : b` | `(c \|\| true) ? a : b` and `(c && false) ? a : b`: each branch forced, the condition still evaluated |
| `NullCoalescing` | `a ?? b` | `a`, and `b ?? a` when `a` only reads a variable, a field or an auto-implemented property |
| `BooleanLiteral` | `true` `false` | each other |
| `Negation` | `!x` | `x` |
| `NegatedDecision` | the condition of a branch or a loop, or a returned `bool`, when it is not a constant | the decision negated, unless it is a negation, a comparison, an `is`, a literal or a ternary's condition, which other families already make the mutant of |
| `Pattern` | `x is P`, `x is T`, and the patterns inside an `is` | the test negated (`x is not P`); a relational pattern's boundary shift and negation, the negation left out when it is the whole pattern; each arm of an `or` or `and` dropped, an `and` keeping its `not null` |
| `StringLiteral` | `"text"` | `""`, and `""` into a non-empty string |
| `Statement` | a statement that only calls something: `f();`, `await f();`, `x?.f();`, `new X();` - unless it assigns a variable (`=` or `out`) or calls a `[Conditional]` method; and a `yield return`, unless it assigns a variable or is the only one of its iterator (#62) | `{ }` - DEC0020 |
| `NumericLiteral` *(opt-in)* | an integer literal in the initializer of a constant, a field or a property, outside a pattern - a `const` only when its own project reads it | `n + 1` and `n - 1`, each only when it keeps the literal's type and fits the type it is used as |
| `Linq` *(opt-in)* | a call to one member of eleven pairs - `Any`/`All`, `Min`/`Max`, `MinBy`/`MaxBy`, `OrderBy`/`OrderByDescending`, `ThenBy`/`ThenByDescending`, `Order`/`OrderDescending`, `Skip`/`Take`, `SkipWhile`/`TakeWhile`, `SkipLast`/`TakeLast`, `Union`/`Intersect`, `Concat`/`Except` - when the counterpart binds to a twin: a method of the same containing type, with the same return type, returned the same way | the counterpart - DEC0024 |

**What it deliberately does not mutate.** The catalogue is selective rather than exhaustive: every
mutant costs a test run, so an operator earns its place by the signal it carries. The inventory below
was measured by running the catalogue over each form rather than read off the language spec.

| Not mutated | Decision |
|---|---|
| `>>>`, `>>>=` | **Future candidate.** High signal - unsigned and signed shift differ only for negative values, which is exactly the case a test suite forgets. |
| `^=` | **Future candidate.** `^` is mutated but its compound form is not, which is an inconsistency rather than a decision. |
| Numeric literals outside an initializer | **Future candidate.** `random.Next(100)` and loop bounds are reached by nothing, but most such literals are noise: to revisit once `NumericLiteral` has been measured - DEC0019. |
| `-x` | **Future candidate**, below the others: most sign errors are already reachable through the arithmetic family. |
| Other LINQ swaps - `First` ↔ `FirstOrDefault` and its kin, `First` ↔ `Last`, `Sum`, `Count` and `Average`, `Reverse` ↔ `AsEnumerable` - and a `Linq` call through `?.` | **Not supported.** The `OrDefault` swaps trade an exception for a `null` and were near-equivalent six times out of six where measured; `First` ↔ `Last` is equivalent on one element; the others change the type or meet an overload ambiguity. A call through `?.` does not bind on its own, and the family is measured before it grows - DEC0024. |
| `+x` | **Not supported.** Removing a unary plus changes nothing, so the mutant is equivalent by construction and can never be killed. |
| `~x` | **Not supported for now.** Removing it changes the value so drastically that any test touching the expression kills it; the mutant is nearly free to write and nearly worthless. |
| `?.`, `as` | **Not supported.** Both mutate into forms that usually throw rather than compute, so they measure whether a test touches the line, not whether it checks the result - and `?.` often does not even compile once the null path is removed. |
| Patterns of a `switch` | **Not supported for now.** Changing one arm can make a later arm unreachable, which the compiler refuses for the whole switch (CS8510) and no check of a single node can see - DEC0018. |
| Assignments and control flow as statements | **Future candidate.** Removing `x = f();`, `return;`, `throw`, `break` or `yield break` can leave a local unassigned or change reachability, which needs a flow analysis `Statement` does not make; a `yield return` needs neither and is removed (#62); assignments to fields are the first to revisit - DEC0020. |
| `switch` arms | **Not supported.** Reordering or removing arms is a structural mutation, not an operator one, and needs a different kind of reasoning about exhaustiveness. |

Three properties hold across all of them. Each replacement is a **new node of the target kind**, not
a swapped token ([RB-001](robustness-backlog-en.md)). Each asks the compiler whether the replacement
would bind before proposing it, so a mutant that cannot compile is never generated
([RB-011](robustness-backlog-en.md)). And each family that could produce a mutant identical in
behaviour to the original declines to: `NullCoalescing` keeps only the left operand, never the right
one, and swaps the two only when the left operand only reads a variable, a field or an
auto-implemented property, so no side effect is silently dropped; `Conditional` skips a ternary
whose branches are the same expression, and still evaluates the condition it forces; and `NegatedDecision` never puts a `!` in front of one
already written.

**Instrumentation has no code of its own, by design.** Because each mutant gets its own compilation
([DEC0002](decisions/0002-one-compilation-per-mutant-en.md)), "instrumenting" a mutant is one call to
`SyntaxNode.ReplaceNode` followed by `Compilation.ReplaceSyntaxTree`. The entire apparatus that a
schemata-based tool needs — injected control helpers, a runtime activation channel, placement
levels, and a compile/rollback loop — does not exist here. This is the largest single simplification
in the design and the reason the rest of it stays small.

## 5. Domain model

Mutants are modelled explicitly. A mutant is never a tuple of strings and integers.

- `MutantId` — an identity, not an `int`.
- `MutatorName` — names the rule that produced the mutation.
- `SourceLocation` — file, line and character span, for reporting.
- `Mutant` — id, mutator name, the original and replacement syntax nodes, and the location.
- `MutantStatus` — what became of a mutant: `Killed`, `Survived`, `Timeout`, `NoCoverage`,
  `CompileError`.
- `MutantOutcome` — what that is *worth*: `Detected` (`Killed`, `Timeout`), `Undetected`
  (`Survived`, `NoCoverage`), `Untestable` (`CompileError`). Kept apart from the status on purpose,
  so no reporter or threshold decides for itself what a timeout or an uncovered mutant means.
- `MutationScore` — a value type that knows how to compute and render itself, so no caller ever
  divides two integers and formats a percentage by hand. It is `Detected / (Detected + Undetected)`.
  Only untestable mutants are excluded, and only because the suite was never asked about them: a
  mutant nothing covers *is* undetected, and excluding it would mean a project could raise its score
  by adding code no test touches.

## 6. Risks

Ordered by expected damage, from the study and from our own probes.

**Critical — false kills from compilation infidelity.** If the reconstructed compilation differs
from the real build in any way (a missing generated `AssemblyInfo.cs` changing the assembly version,
a missing reference, a wrong preprocessor symbol), the tests fail for reasons unrelated to the
mutation and every mutant is reported `Killed`. A mutation tool that always says "Killed" is worse
than no tool, because it is silently reassuring. *Mitigated by DEC0005: the baseline is emitted
through the same path and must run green before any mutant is considered.*

**Critical — silent equivalent mutants from tree rewriting.** Verified first-hand: replacing the
operator *token* of a `>=` with `>` leaves the parent node's kind as `GreaterThanOrEqualExpression`.
Roslyn emits from the node kind, so `ToFullString()` shows `age > 18` while the IL is unchanged. The
mutant is silently equivalent and is reported `Survived`. *Mitigated by replacing whole nodes, and
by a regression test asserting the emitted IL actually changes.*

**High — empty `CscCommandLineArgs`.** If MSBuild considers the project up to date it skips
`CoreCompile` and returns no arguments; `CSharpCommandLineParser` then happily produces a default
compilation with no sources and no references. *Mitigated by forcing the target to re-run and
asserting the argument list is non-empty and contains `/out:` and `/target:`.*

**High — an MSBuild target restoring the pristine assembly.** `dotnet build` and `dotnet test` both
copy the source project's output over an injected mutant. *Mitigated by never invoking either after
injection: the test executable is launched directly.*

**Medium — a zero-match test filter reads as success.** The xUnit console runner exits `0` when a
filter matches no tests, which would classify a mutant as `Survived`. *Mitigated by reading the
structured result file and requiring a positive executed-test count, rather than trusting the exit
code alone.*

**High — mutations and instrumentation that break definite assignment.** A pattern or `out` variable
is definitely assigned only *conditionally*. Mutating the expression that declares it changes when
its parts are evaluated, and wrapping that expression in the coverage probe passes the state through
a method call; either way the project stops compiling. Found within seconds of running the tool on
its own source, where guard clauses of this shape are everywhere. *Mitigated by not mutating an
expression that declares a variable, which also removes it as an instrumentation site —
[RB-016](robustness-backlog-en.md).*

**Medium — a site whose value the probe cannot accept.** `Hit<T>` cannot take a ref struct, a pointer
or `void` as its type argument, and a conditional expression over two `Span<T>` has exactly that
type. *Mitigated by leaving those sites uninstrumented and testing their mutants against the whole
suite — [RB-017](robustness-backlog-en.md).*

**Medium — mutations that introduce an infinite loop.** Not reachable by milestone 1's single
mutator, but the timeout must exist before the catalog grows. *Deferred to M2, with the baseline
duration already recorded for the budget.*

## 7. Roadmap position

Milestone 1 was one project pair, one mutator (`>=` becomes `>`), one mutant, executed for real.
M2 grew the catalogue to six families, M9 to eleven. M3 handles real solution structure: several
test projects, several projects under test, project references followed transitively, and a
framework pinned per project. M6 tests mutants in parallel, each worker in a private copy of the test output directory. M4 and M5
discover the tests and measure which ones reach which mutants, so uncovered mutants are never run and
the rest run only what can kill them. M7 reports: live progress, findings grouped by file, and a JSON
report for anything that is not a person. M8 makes it usable as a quality gate: an opt-in
`--break-at` threshold and exit codes that separate a weak test suite from a broken run. M9
grows the operator catalogue to eleven families - selective rather than complete, with the
omissions listed above decided rather than overlooked. M10 makes it a tool rather than an engine: packaged as
`dotnet killmutants`, given the `--exclude` a real repository needs, and — the point of the milestone
— run against KillMutants' own source, which found RB-016 within seconds. M11 makes its output
actionable: what each mutator family cost and caught, the `--mutators` and `--without` that act on
that, and `[ExcludeFromCodeCoverage]` respected. M12 lets a project keep those choices in
`killmutants.json` rather than in a shell command, so the catalogue behind a score is versioned with
the code it scored. M13 makes it usable on a pull request rather than nightly: `--since` judges only
what a change touched, and — because a population defined by a diff has no percentage worth printing
— reports findings and a binary verdict instead of a score. DEC0010 argues that; DEC0011 settles the
selection, which is the interesting part: changed production code precisely, changed *tests*
conservatively, and the project graph read at both revisions so that removing a project reference in
the change being judged does not delete the answer along with the question.

**What running it on itself measures.** 384 mutants over `KillMutants.Core`, 6.8 minutes on four
cores: 106 killed, 111 survived, one killed by timeout, 166 uncovered, none failing to compile — a
mutation score of 27.86%. Two numbers deserve their caveats rather than a headline. The uncovered
mutants are largely an artefact of the run's own configuration: it excludes the end-to-end suite,
which is what exercises most of the discovery, analysis and execution code, so those mutants are
uncovered *by the suite that was run* rather than untested. And of the survivors, a large share are
`StringLiteral` mutants on error messages nothing asserts on — true findings, but the least useful
ones per unit of run time, and the first evidence about where the catalogue earns its keep on real
code. The score is reported here as a measurement, not as a verdict on the test suite.

**Where a run's time actually goes.** Measured over `KillMutants.Core` on four cores, 384 mutants,
72 test methods, 6.8 minutes end to end:

| Phase | Time | Share |
|---|---|---|
| Discovering five projects | 2.2 s | 0.5% |
| Building the test projects | 3.1 s | 0.8% |
| Reading the compiler command lines and building the compilations | 5.2 s | 1.3% |
| Verifying the baseline | 22.1 s | 5.5% |
| Measuring coverage, one run per test method | 73.9 s | 18.2% |
| Testing the mutants | 299.0 s | 73.7% |

Two things follow, and both are reasons *not* to change anything yet. Coverage measurement is not the
bottleneck at this shape: it costs one process launch per test, about 1.0 s each here, and buys the
skipping of 166 uncovered mutants outright. And it scales with the *test* count while the mutant
phase scales with the mutant count, so the strategy stops paying only when tests greatly outnumber
mutants — a suite of a thousand tests would spend seventeen minutes measuring. That is the number to
watch, and until a real project reaches it the exact attribution one run per test buys is worth more
than the time a cleverer scheme would save. See
[DEC0007](decisions/0007-measure-coverage-with-a-type-preserving-probe-en.md).

**Which families are worth their time, and who decides.** The families do not carry equal
signal, and running the tool on itself measured the gap: `Comparison`, `LogicalOperator` and
`Arithmetic` detect 45% to 55% of the mutants they produce, while `StringLiteral` and
`BooleanLiteral` together account for half of all mutants generated and detect 10% to 15% of them —
error messages and flags nothing asserts on. Half the run's cost for a third of its survivors.

That is not a reason to delete them: a surviving `StringLiteral` mutant is a true finding, and on a
project that asserts on its messages it is a useful one. It is a reason to *report the split* and let
the user act on it, which is what `--mutators` and `--without` are for. The report shows what each
family cost and caught, so the choice is made against a project's own numbers rather than against
this one's. Dropping those two here takes the run from 413 mutants in 7.1 minutes to 207 in 4.1, and
the survivors to read from 129 to 62.

**Which makes one thing worth saying out loud:** a score is only comparable to another score from the
same catalogue. That same change moves the number from 28.81% to 43%, and the tests did not improve —
a different question was asked. A CI job should pick a catalogue and keep it; the JSON report lists
the families that actually ran, so a consumer can tell which question was answered.

M11 also stops mutating anything marked `[ExcludeFromCodeCoverage]`. The attribute is a statement of
intent — this code is not part of what the tests are expected to cover — and since uncovered and
surviving mutants both weigh on the score, ignoring it did not merely clutter the report, it moved
the number.

**Where a project's habits live.** M12 reads `killmutants.json` from the directory the run was
pointed at. Every setting mirrors a command-line option and anything given on the command line wins,
so the file states the habit and the command line states the exception. The reason it exists is the
paragraph above: a score only means something against the families that produced it, so a job that
picks its catalogue in a shell command has put a number nobody can reproduce into its logs. Keeping
it beside the code versions the question along with the answer.

Two rules earn their place. A misspelt key stops the run rather than being ignored — the same refusal
the command line makes for a misspelt mutator family, and for the same reason. And every command-line
option is parsed as *nullable*: `--configuration Release` and saying nothing have to be told apart,
or the defaults would silently outrank a file that never mentioned them.

**Two output streams, on purpose.** Progress goes to standard error and the report to standard
output, so `killmutants > report.txt` captures the report without the progress line threaded through
it. On a terminal the progress line is rewritten in place; when the stream is redirected each phase
is announced once instead, because thousands of carriage returns in a CI log help nobody. A phase
that counts is also written again with its count, at most once a minute, because an announcement
alone left a cancelled job's log unable to say whether testing had stopped at 800 of 816 mutants or
at the first: an hour costs sixty lines, and a phase that makes no progress adds none.

**A run that does not reach its end.** A run keeps its verdicts in memory and writes its reports
when it returns, so a run cut short by a CI timeout or by Ctrl-C used to leave nothing at all. The
session now tells a *stop* from a *cancellation*: both end whatever is in flight, but a cancelled
run is abandoned, while a stopped one returns a report of the verdicts it had reached, marked as
stopped, with no score and no gate passed (DEC0013). The command line asks for a stop when
`--stop-after`'s deadline comes or on the first SIGINT or SIGTERM. A run counts as stopped only when
a verdict is missing: a stop that lands while kills are being re-verified, once every mutant has its
verdict, leaves a complete report, which says only how many kills were re-verified. A verdict
becomes final in one place, `VerdictLedger` — at once for most statuses, and for a timeout only once
it has been confirmed alone — and a verdict reached while the run is coming down is dropped, because
a test host the stop killed reads as a crash and a crash counts as a kill. Each final verdict is
also told to an observer as it lands, which the command line appends to a JSON Lines log beside the
report (DEC0014), so even a run killed outright, which hears nothing, leaves behind what it judged.

**A run that belongs to a series.** `--resume <id>` names a series, and the run keeps a plan beside
the report (DEC0016): what identifies the run — the commit, whether the tree is exactly it and which
directory of it the run was pointed at, the settings, the tool, and once the mutants are generated
the population, the project graph and the xUnit version — and every mutant it selected, so that plan
minus log is what remains, readable without the tool. The first plan of a series is written before
discovery, with no mutants yet, so a run stopped during the build leaves one; each run records where
in the log it began, and a later run recomputes every earlier run's count from the log rather than
from a plan a killed run never got to rewrite. A later run of the same name on the same identity
inherits the log's final verdicts
into the ledger before any mutant is tested — timeouts excepted, since each is final only under the
budget that confirmed it, and kills no test is named for, since a crash is the one verdict a stop can
manufacture — and tests only the rest, so a project with nothing left pays neither sandbox, baseline
nor coverage, and a project larger than one budget is finished over several runs. The run that
reaches the last verdict reports the series complete, with a score, because its population is the
whole codebase at one commit and not the front of a run (DEC0015). The identity is compared twice,
and the two halves end differently: the half the command line knows before the run — read from git
with the tool's own files left out of the dirtiness read — decides what becomes of the log: continued
for the series of the same name on the same identity, the run refused for that name on another
identity, emptied when there is no plan or it is another series'; the half only the run knows is
compared at selection, and a difference there is refused with the plan, the log and the report as
they were — the command line puts back the report it had cleared — because what differs after
everything else matched is not something the user did. Git stays where it was: the command line
reads the code state and hands the core an opaque name, as the partial run's `comparedTo` already is.

**Why sandboxes rather than a shared, warmed-up test host.** Reusing one host across mutants is the
most tempting optimisation available and the one we refuse: it is the source of Stryker's
longest-standing correctness complaint, where process-global state leaks between mutants and inflates
scores. A private output directory per worker costs a directory copy and some disk, and buys the
guarantee that no two mutants can ever observe each other. It also means KillMutants never writes
into the developer's build output at all.

**The ordering rule M3 established.** Build every test project, then read every compiler command
line, then inject. MSBuild must not run before the build, because reading a command line relies on
its output; and must not run after injection, because `dotnet build` and `dotnet test` both copy the
pristine assembly back over a mutant. See RB-012 in the robustness backlog.

Nothing in M1 blocks these. Test selection (M5) narrows what `ITestRunner` is asked to run.
Parallelism (M6) is available because each mutant is an independent assembly and an independent
process — the property that one-compilation-per-mutant gives us for free.

### What later milestones must handle, already verified

An adversarial review of the committed M1 established these on this machine. They are recorded now
because each is cheap to plan for and expensive to discover late.

- **M2 needs a do-not-mutate list, and it is a correctness requirement rather than a refinement.**
  C# bakes `const` values and default parameter values into the *call site* at compile time.
  Mutating `const Limit = 18` to `99` in the library and swapping the assembly leaves an
  already-compiled test project still reading `18`. Such a mutant can never be killed, so mutating
  those constructs would manufacture guaranteed false survivals and silently depress the score.
- **M6's blocker is injection, not compilation.** `AssemblyInjection` holds one path and
  `MutationTestSession` hoists a single `using` above the mutant loop, so N concurrent mutants need
  N sandboxed output directories. Measured with four sandboxes: 639 ms against 2,235 ms sequential,
  a 3.5x gain with correct independent verdicts. Emission itself parallelises well — 3.76 ms per
  emit at one thread, 0.85 ms at four — which strengthens DEC0002 rather than straining it: the
  term schemata would optimise shrinks as the run scales out.
- **M5 and M6 collide, and the collision is in the data model.** xUnit test unique IDs are derived
  from the assembly *path*, not its content: byte-identical sandbox copies produced different UIDs.
  Per-mutant sandboxing and UID-based test selection are therefore mutually exclusive as stated. The
  question "what is the coverage map keyed on" has to be answered before either is built.
- **Coverage needs a mechanism this design does not yet name.** With nothing injected there is
  nothing observing that a test reached a given mutation site. `-automated` yields per-*test* events,
  which is a different problem from per-*mutation-site* reachability. M5 must choose its source
  deliberately — a separate instrumented pass, or external coverage data mapped onto mutation
  spans — rather than assume the runner already provides it.
- **Mutant numbering runs across a whole session.** One generator serves every project, so
  identifiers never repeat; a generator per project would restart at `M1` for each and make the
  report ambiguous. Done, and pinned by a test.
