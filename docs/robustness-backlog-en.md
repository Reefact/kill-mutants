# Robustness backlog — edge cases inherited from Stryker.NET

Stryker.NET has been in the field for years, and its scar tissue is knowledge. This file is how we
inherit that knowledge **as specifications and tests** rather than as architecture.

The method for every piece of Stryker complexity that looks strange, defensive or historical:

1. find the tests that cover the behaviour;
2. find the associated GitHub issues;
3. find, where it informs, the PR or commit that introduced or fixed it;
4. understand the bug, edge case or constraint that motivated it;
5. only then decide whether that constraint still exists for C#, modern .NET and xUnit 4.

Complexity is not dismissed because it looks excessive. It is dismissed only once we can name what
it was defending against and show that the threat is gone. When the threat remains, we reproduce it
as our own regression test, even though our implementation of the mechanism is entirely different.

**Status vocabulary.** `COVERED` — a KillMutants test fails if the behaviour regresses.
`OPEN` — understood, reproduced, not yet handled. `ACCEPTED` — understood and deliberately not
handled, with the reason recorded.

---

## RB-001 — A mutation must change the node kind, not only the operator token · COVERED

**What Stryker does.** `BinaryExpressionMutator` always constructs a fresh node via
`SyntaxFactory.BinaryExpression(kind, …)` and only then re-attaches the original operator token's
trivia (`Mutators/BinaryExpressionMutator.cs:60-62`). Their design note states the reason outright:
*"Changing the token changes the text representation, but the compiled version will retain the
original operator!"* (`docs/technical-reference/Mutation Orchestration Design.md:33`).

**Why it exists.** Roslyn binds and emits from the node kind. Swapping only the token produces a
tree that *prints* as `age > 18` while emitting the IL of `age >= 18`.

**Does it still apply?** Yes, entirely. This is a property of Roslyn, not of any legacy constraint.
Reproduced independently during this project's design, at IL level: the token-swap variant emitted
`clt`, identical to the original, while the node replacement emitted `cgt`.

**Why it is the worst failure mode we know of.** The mutant compiles, the report looks right, the
tests pass, and it is recorded **Survived** — an invented gap in the user's test suite. Baseline
verification (ADR-0005) cannot catch it, because that guards against false *kills*.

**Our tests.** Every family in the catalogue carries an
`Every_replacement_carries_the_kind_it_prints` test — `ComparisonOperatorMutatorTests`,
`LogicalOperatorMutatorTests`, `BooleanLiteralMutatorTests` — and the behavioural guard sits in
`ProjectCompilationTests.A_mutant_emits_an_assembly_that_actually_differs_from_the_baseline`. The
rule is stated in the `IMutator` contract so every new mutator inherits it, and enforced structurally
by `BinaryOperatorMutator`, which builds replacements by kind on behalf of the whole family.
**Every mutator added to the catalogue must be covered by an equivalent assertion.**

---

## RB-002 — Source generators contribute code that is not on the source list · COVERED

**What Stryker does.** Runs the generator driver, and re-runs it after every `ReplaceSyntaxTree`
(`Compiling/CsharpCompilingProcess.cs:360-364`). It also detects projects pinning a newer compiler
than its own and logs it by name (`ReferencesNewerCompiler`,
`Buildalyzer/IAnalyzerResultExtensions.cs:115-120`).

**Why it exists.** `CscCommandLineArgs` lists generators under `/analyzer:` but does **not** list
their output among the sources — the compiler produces it during the build.

**Does it still apply?** Yes, and more than before: `[GeneratedRegex]`, `[JsonSerializable]`,
`[LibraryImport]`, Mapperly, Refit and ASP.NET Core minimal APIs are all generator-based. Reproduced:
a project with a `[GeneratedRegex]` partial property failed the baseline with `CS9248`, while
building perfectly under `dotnet build`. Even our trivial fixture's command line carries eight
`/analyzer:` assemblies; the generator project carries seven actual generators.

**Our behaviour.** `SourceGenerators` loads every generator named on the command line and runs it
through `CSharpGeneratorDriver`, with the real `.editorconfig` / `.globalconfig` files the compiler
was given and the project's `AdditionalFiles`, so generators reading MSBuild properties see the same
values they would during a real build.

**Regenerated per mutant, and measured rather than assumed.** Generator output can depend on the
code being mutated, so the driver is re-run for each mutant instead of its output being reused.
Measured on the seven-generator project: the first driver run costs about a second, every later one
**1.4 ms**, against 60 ms to emit and roughly 600 ms to run the tests. Correctness is essentially
free here, so no approximation was needed. This lands on the same behaviour as Stryker, but for a
reason verified on this platform rather than inherited.

**Generated code is compiled, never mutated.** The generated trees must be in the compilation the
mutators read — a semantic model that cannot see generated types answers the binding question
wrongly — but they are excluded from mutation by path. Pinned by a test asserting every mutant comes
from the hand-written file, since the generated regex engine is full of comparisons and arithmetic
that nobody wrote and nobody can fix.

**An analyzer that cannot be loaded is named.** Usually a project pinning a newer Roslyn than
KillMutants runs on, which silently contributes nothing. Those assemblies are recorded and reported
with our Roslyn version attached, rather than surfacing as "KillMutants could not compile your
project".

**Our tests.**
`MutationTestingEndToEndTests.A_project_that_depends_on_a_source_generator_is_mutated_and_tested`,
which fails with `CS9248` if the driver stops running.

---

## RB-003 — A crashed test host must cost one mutant, not the run · COVERED

**What Stryker does.** Keeps a fixture that exists solely for this
(`integrationtest/…/StrykerFeatures/StackOverflow.cs`), a dedicated branch in
`Mutants/Mutant.cs:45-51`, and an `IsAlive` flag for "initialised but the process is gone"
(`AssemblyTestServer.cs:44-51`).

**Why it exists.** A mutation can remove a recursion base case. The resulting
`StackOverflowException` cannot be caught and kills the process before any result is written.

**Does it still apply?** Yes. It is a property of the CLR, not of VSTest.

**Our behaviour.** The runner returns `TestRunOutcome.FromCrash` instead of throwing. The session
then decides by context: during baseline verification a crash aborts the run with a clear message,
because nothing downstream would be trustworthy; for a mutant it is recorded `Killed`, since the
baseline already proved the host runs cleanly unmutated, so the crash is attributable to the
mutation.

**Our tests.** `XUnitTestRunnerTests.A_host_that_writes_no_result_file_is_reported_rather_than_thrown`,
`TestRunOutcomeTests.A_crashed_run_is_neither_a_pass_nor_an_empty_run`.

---

## RB-004 — Warnings-as-errors must be fully neutralised · COVERED

**Why it matters.** A mutation frequently makes live code unreachable (CS0162) or a variable unused
(CS0219). If those still fail the compilation, the mutant is recorded `CompileError` and dropped from
the denominator, silently understating the score for a reason unrelated to the tests.

**The trap.** `WithGeneralDiagnosticOption(Default)` clears `/warnaserror+` but leaves
`SpecificDiagnosticOptions` untouched. Verified against Roslyn 5.9: after that call,
`/warnaserror+:CS0162,CS0219` still maps both to `Error`. `<WarningsAsErrors>nullable</WarningsAsErrors>`
is common in real projects, and our own fixture's command line carries
`/warnaserror+:NU1605,SYSLIB0011`.

**Our behaviour.** Every entry whose value is `Error` is demoted to `Warn`. Suppressions from
`/nowarn:` are preserved — the user silenced those deliberately, and honouring that cannot cost us a
mutant.

**Our tests.** `WarningsAsErrorsTests`.

---

## RB-005 — Compile-time constants cannot be mutated observably · COVERED

**What Stryker does.** Maintains a do-not-mutate list covering `const`, attribute arguments and enum
members.

**Why it exists.** C# copies these values into every *call site* at the consumer's build time.

**Does it still apply?** Yes — it is a language rule. Verified: mutating `const Limit = 18` to `99`
and swapping the assembly left an already-compiled consumer still reading `18`. A nuance worth
recording: the library's *own* code does observe the new value; it is specifically the consumer —
which is the test project — that does not.

**Impact.** Such a mutant is guaranteed to survive however good the tests are. Generating it would
manufacture a gap the user cannot act on and depress the score for no reason.

**Our behaviour.** `MutationSite.IsObservable` excludes `const` fields and locals, default parameter
values, attribute arguments and enum members. They are skipped, not reported.

**Our tests.** `MutationSiteTests`.

---

## RB-006 — Injection is not crash-safe · COVERED

**Why it exists.** A run killed by SIGKILL or a cancelled CI job cannot clean up after itself, so it
leaves a mutated assembly in the developer's output directory. Stryker has the same wound and only
logs it (`ProjectComponents/TestProjects/TestProjectsInfo.cs:51-58`).

**How we first fixed it, and why that changed.** Taking custody of an assembly restored any
abandoned backup before doing anything else. That worked, but it was a rule to remember.

Parallelism made it unnecessary. Each worker now runs from a private copy of the test output
directory, so **KillMutants never writes into the developer's build output at all**. A run that dies
halfway leaves nothing behind but a temporary directory. The failure mode is gone by construction
rather than by cleanup, which is the better kind of fix: there is no longer a rule to forget.

---

## RB-007 — A multi-targeted project yields one command line per framework · COVERED

**Why it exists.** Asking MSBuild about a project that targets several frameworks without saying
which one gives an answer for an unspecified one. Mutants could then be emitted against a framework
the test project never loads.

**Our behaviour.** A project under test is always resolved against the framework of the test project
that reaches it, pinned explicitly on the MSBuild query. A *test* project that targets several is
refused with a message naming them, rather than silently picking one and reporting a score for a
framework the user did not choose — each would need its own run, its own output and its own verdict.

---

## RB-008 — Generated-file exclusion is path-based only · OPEN

`MutantGenerator` skips `.g.cs`, `.g.i.cs` and anything under `obj/`. Files produced by T4, designers,
protobuf or a custom `BaseIntermediateOutputPath` are still mutated, producing findings against code
nobody wrote. Reading `<auto-generated>` headers would be more honest.

---

## RB-009 — Retaining every mutant's artefacts will not scale · OPEN

Mutated syntax trees, emitted byte arrays and per-mutant diagnostics are all held for the whole run.
Real solutions reach tens of thousands of mutants. Not a problem at current scale; it becomes one at
M3.

---

## RB-010 — A mutation can turn a terminating loop into an endless one · COVERED

**Why it exists.** Stryker derives a per-session timeout from the baseline run and force-kills the
test host on expiry, for one reason: a mutation can make a loop never finish. No dedicated mutator is
needed to reach this. The arithmetic family already does it — rewriting `value = value + 1` to
`value - 1` makes a `while (value <= limit)` condition permanently true.

**Our behaviour.** `TimeoutPolicy` derives the budget as `baseline x factor + margin`, defaulting to
three times the baseline plus thirty seconds. The default is deliberately generous: a mutant wrongly
reported as timed out hides a real gap in the tests, which is worse than waiting. `ProcessRunner`
kills the entire process tree on expiry, and the mutant is recorded `Timeout` — counted as a
detection in the score, because a mutation that hangs the suite did change observable behaviour.

**The trap, met head-on while writing the test.** The first fixture used `int` counters and the
mutant was reported *killed*, not timed out. The decrementing counter reaches `int.MinValue`, wraps
to `int.MaxValue`, and the loop condition goes false — so it finishes after about two billion
iterations, in roughly seventeen seconds. Widening the counters to `long` puts the wrap around nine
quintillion iterations away. The lesson generalises beyond the fixture: **many mutants that look like
infinite loops are merely very slow ones**, which is an argument for the budget being a deadline
rather than an attempt to detect non-termination.

**Our tests.** `MutationTestingEndToEndTests.A_mutation_that_never_terminates_is_recorded_as_timed_out`
runs the real tool against a real project and asserts the arithmetic mutant times out while the other
three are killed. `ProcessRunnerTests.A_process_that_never_finishes_is_killed_and_reported_as_timed_out`
pins the kill itself, and `TimeoutPolicyTests` the arithmetic of the budget.

---

## RB-011 — A mutation that cannot compile is cost without signal · COVERED

**Why it matters.** `"a" + "b"` is concatenation; `"a" - "b"` does not exist. An arithmetic mutator
that rewrote it would produce a mutant that fails to emit — a correct outcome, but a useless one that
costs analysis and clutters the report.

**The general answer, rather than a list.** Every binary mutator asks the compiler whether the
replacement would bind, via `GetSpeculativeTypeInfo`. One rule rejects string concatenation,
user-defined types declaring only one operator of a pair, and every case nobody has thought of yet —
while allowing delegates, where both `+` and `-` exist.

**The trap inside the trap.** The test must be on the resulting **type**, not on the symbol. Verified
against Roslyn 5.9: `a && b` rewritten to `a || b` binds to a *null symbol* — the conditional
operators on `bool` have no operator method — while still yielding type `bool`. A symbol-based check
compiles, passes a casual review, and silently discards every logical mutant. Our own family tests
caught it.

**Our tests.** `ArithmeticOperatorMutatorTests.String_concatenation_is_not_mutated`,
`A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated`,
`A_type_that_declares_both_operators_is_mutated`, and the `LogicalOperatorMutatorTests` suite, which
is what fails if the check regresses to the symbol.

---

## RB-012 — Reading the compiler command line can destroy the build output · COVERED

Found while making multi-project solutions work, and the single most surprising thing in this file.
Two switches that look like sensible isolation are actively harmful, and neither fails visibly on a
single-project solution.

**`IntermediateOutputPath` is global.** Redirecting it to keep generated artefacts out of the user's
`obj` directory propagates to every referenced project, so the command line points at reference
assemblies in a directory where the compiler was never allowed to run. Any project with a project
reference then fails with `FileNotFoundException` before it can be mutated.

**`CopyBuildOutputToOutputDirectory=false` deletes the built assembly.** With the copy suppressed and
the compiler skipped, MSBuild's incremental clean sees an assembly it did not write and removes it
from `bin`. Verified: querying `Core` deleted `Core/bin/Release/net10.0/Core.dll`, and the next
project's query then failed trying to copy the reference that had just vanished.

**Our behaviour.** Neither switch is used. `CoreCompile` is forced to re-run by deleting the cache
file its incremental check reads — a file MSBuild regenerates — and the query runs *after* the real
build, so the intermediate assembly still exists, the copy succeeds and nothing is cleaned.

**Where this knowledge lives, and why inheriting it was not enough.** Stryker contains none of these
properties; it delegates the whole problem to Buildalyzer, whose `MsBuildProperties.DesignTime` is a
set of about fifteen global properties meant to be used together — including `SkipCopyBuildProduct`,
`BuildProjectReferences` and `UseCommonOutputDirectory` alongside the
`CopyBuildOutputToOutputDirectory` we reached for alone. Taking one property out of a coordinated set
is what produced the bug.

But applying that set wholesale does **not** fix it, which was measured rather than assumed: with the
full canonical design-time properties, one fixture project still returned an empty command line and
the built assemblies were still deleted from `bin`. Buildalyzer's constraint is not ours. It analyses
projects and never needs their build output to survive; KillMutants analyses a project and then runs
tests against those very artifacts. The lesson is therefore sharper than "read the dependency you
replaced": when you drop a dependency you inherit its problem space but not its scar tissue, and its
scar tissue may not even fit your problem.

**The ordering is now a rule, not an accident.** Build every test project, then read every compiler
command line, then inject. MSBuild must not run before the build, because the query depends on its
output; and must not run after injection, because `dotnet build` and `dotnet test` both copy the
pristine assembly back over the mutant.

**Our tests.** `MutationTestingEndToEndTests.Several_projects_and_several_test_suites_are_all_covered`,
which runs from a clean checkout and fails if either switch comes back.

---

## RB-013 — The timeout budget is measured alone but spent under load · OPEN

The per-mutant budget is derived from a baseline run that happens with nothing else running, while
mutants are then tested with up to `--parallel` siblings competing for the machine. A healthy but slow
mutant could exceed its budget purely because of that contention and be recorded `Timeout` — counted
in the score as a detection, so the effect is to *inflate* the score rather than to depress it.

The default settings make this unlikely: three times the baseline plus a thirty-second margin, with
half the logical processors used by default. It is recorded rather than fixed because the right
answer is not yet obvious — measuring the baseline under representative load, re-timing a suspected
timeout on an idle worker, and simply widening the margin are all plausible, and choosing between
them needs data from a real project rather than from a fixture.

---

## RB-014 — Process startup is now the floor · ACCEPTED

With coverage-driven selection in place, a mutant's run costs roughly 0.5 s to launch a test host
against 0.12 s of actually testing. Selecting fewer tests can no longer help much; the launch
dominates.

The obvious next lever is a warm, reused test host, and it is **deliberately refused**. Stryker, which does reuse hosts, needs explicit points at which
they are reset (`MicrosoftTestPlatformRunnerPool.cs:96,140`); we would need the same discipline, and
an assembly already loaded by a warm process is not re-read from disk at all. A tool whose whole purpose is to tell the
truth about a test suite cannot buy speed with a mechanism that quietly reports mutants as killed
when they were not.

Recorded as accepted rather than open: the cost is understood, the alternative is understood, and
the trade has been made on purpose.

---

## RB-015 — A deletion mutator can change the type, not only the value · COVERED

Found while adding the `NullCoalescing` family in M9, and the reason that family is not the one-line
rewrite it looks like.

**Why it matters.** `a ?? b` is very often there to *remove* nullability rather than to supply a
fallback value. Dropping the fallback then leaves an expression of a different type:
`int total = count ?? 0` mutated to `int total = count` is a hard error (CS0266), not a mutant. Worse,
the reference-typed case is not symmetric with it — `string s = name ?? ""` mutated to `string s = name`
compiles, because the nullability complaint is a warning, and warnings are already neutralised for
mutant compilations by RB-004. So a naive rule silently produces useful mutants in one half of the
cases and compile errors in the other.

**The rule.** Classify the conversion the compiler would have to make from the left operand alone to
whatever the surrounding code expected: `ClassifyConversion(coalesce.Left, GetTypeInfo(coalesce).ConvertedType)`,
and propose the mutation only when that conversion exists and is implicit. This keeps the widening
cases mutable (`object o = text ?? fallback`) and rejects exactly the nullability-removing ones.

**Why not the mirror mutation.** Rewriting `a ?? b` into `b` is tempting for symmetry, and is
deliberately not done: it discards the left operand and any side effect it carries, which turns a
missing-coverage signal into an unrelated behaviour change. The surviving mutant would be true but
uninformative.

**The neighbouring case, same milestone.** `Conditional` swaps the branches of `c ? a : b`, and a
ternary whose branches are the same expression would yield a mutant that behaves exactly like the
original: guaranteed to survive, for a reason that says nothing about the tests. Those are skipped.
The binding check for this family also had to be *weaker* than the binary one: a conditional need not
have a natural type — `flag ? 1 : null` only acquires one from its target — so a null type must not be
read as a failure the way `BinaryOperatorMutator` reads it.

**Our tests.** `NullCoalescingMutatorTests.A_fallback_that_removes_nullability_is_not_dropped`,
`A_left_operand_that_widens_to_the_expected_type_is_dropped`,
`ConditionalExpressionMutatorTests.Identical_branches_are_not_swapped`,
`A_target_typed_conditional_is_mutated`, and the end-to-end
`Every_mutator_family_is_exercised_against_the_fixture`, which fails if any family stops producing
mutants against a real project.

