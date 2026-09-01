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

**What the guarantee actually says.** Not "the mutated syntax differs" — that is the very thing a
token-only rewrite gets right — but *the emitted program differs*. The check compares the whole
emitted assembly rather than method bodies alone, because a changed string literal can leave the IL
byte-identical (`ldstr` keeps its heap index) while the program plainly differs; comparing the file
covers the metadata heaps too.

**What that comparison depends on, and how it once failed.** `CSharpCompilationOptions` defaults
`Deterministic` to `false`, and the corpus built its snippets without setting it. Measured against
Roslyn 5.9: two emits of an *identical* program then differ, because the module version id and the
header timestamp are generated afresh each time. So the comparison reported every mutant as
different — including one that changed nothing — and the guarantee passed while proving nothing. The
real pipeline was never affected: the compiler command line MSBuild reports carries
`/deterministic+`. With determinism the file is a function of the program alone; measured, the same
program emits byte-identical assemblies through reformatting, an added comment and a changed file
path, and no debug stream is emitted, so nothing carries source positions either.

**Our tests.** Every family in the catalogue carries an
`Every_replacement_carries_the_kind_it_prints` test — `ComparisonOperatorMutatorTests`,
`LogicalOperatorMutatorTests`, `BooleanLiteralMutatorTests` — and the behavioural guard sits in
`ProjectCompilationTests.A_mutant_emits_an_assembly_that_actually_differs_from_the_baseline` and in
`CatalogueCorpusTests.Every_proposed_mutant_changes_the_emitted_program` across the corpus. The rule
is stated in the `IMutator` contract so every new mutator inherits it, and enforced structurally by
`BinaryOperatorMutator`, which builds replacements by kind on behalf of the whole family.
**Every mutator added to the catalogue must be covered by an equivalent assertion.**

**And the guard on the guard.** A check that only ever sees real mutants pass is not evidence, so two
tests hold it up. `The_same_compilation_emits_the_same_bytes_twice` asserts the precondition rather
than assuming it. `A_token_only_rewrite_is_rejected_although_the_syntax_shows_a_mutation` builds this
entry's exact mistake on purpose — a swapped operator token on a node that keeps its original kind —
asserts that the syntax plainly shows `>` where `>=` was, and requires the check to answer that
nothing changed. Removing `WithDeterministic(true)` fails both of them, and only them: the corpus
guarantee itself still passes, which is precisely how the hole stayed invisible.

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

## RB-008 — Generated-file exclusion is path-based only · COVERED

**Why it matters.** Mutating generated code reports findings the developer cannot act on: the answer
to "this mutant survived" is to change a template, a schema or somebody else's generator. It also
buries the real findings under hundreds of them.

**What was missing.** The rule was `.g.cs`, `.g.i.cs` and anything under `obj/` — names and
directories, which only catch what we thought of. A project with a custom
`BaseIntermediateOutputPath` puts its intermediates somewhere else entirely; T4 writes its output
beside the template; protobuf and the resx designers use their own conventions; and the next
generator will call its output whatever it likes.

**What Stryker.NET does.** `GeneratedCodeFilterExtension` (`MutantFilters/`) recognises
`*.designer.cs` by name and, more importantly, an `<auto-generated` or `<autogenerated` marker in the
leading comment of the file — a rule its header credits to StyleCopAnalyzers. Neither their rule nor
ours was a superset of the other: they had the header and `.designer.cs`, we had `.g.cs` and `obj/`.

**The rule now.** The union, implemented from the convention rather than from their code. The header
is the part that closes the entry, because it travels *with* the file instead of describing where the
file sits — which is exactly why the convention exists: the C# compiler reads it to suppress analyser
warnings, and T4, protobuf, the designers, XSD and EF all emit it. It is honoured only at the very
top of the file, before any code, as the convention specifies; a comment further down is a comment.

**Our tests.** The `SourceFileTests` suite, in particular
`A_file_that_declares_itself_generated_is_recognised_wherever_it_lives` — an ordinary name, outside
`obj`, recognised on its header alone — and `The_header_only_counts_at_the_top_of_the_file`.

---

## RB-009 — Retaining every mutant's artefacts will not scale · ACCEPTED

Recorded as a worry, then measured. The worry was wrong about what is retained, and the measurement
says so plainly.

**What was assumed.** "Mutated syntax trees, emitted byte arrays and per-mutant diagnostics are all
held for the whole run." Emitted assemblies are not: `EmitWith` returns them to a local, the sandbox
writes them to disk, and nothing keeps a reference. What a finished mutant actually retains is its
two syntax nodes — whose green nodes are shared with the tree the compilation already holds — a
status, and a diagnostics string only when it failed to compile.

**The measurement.** Resident set sampled every two seconds through a full run over
`KillMutants.Core`, 384 mutants on four cores:

| Point in the run | RSS |
|---|---|
| Start | 39 MB |
| After building the test projects | 41 MB |
| After reading the compiler command lines | 47 MB |
| After building the Roslyn compilation and running the generators | 210 MB |
| Start of the mutant phase | 321 MB |
| End of the mutant phase | 399 MB (peak 402 MB) |

The shape is what settles it. Within the mutant phase, RSS reaches 399 MB after about 100 seconds
and then stays between 396 and 402 MB for the remaining 255 seconds — roughly three hundred more
mutants at no additional cost. Retention linear in the mutant count would have climbed throughout;
it does not. The rise that does happen is the heap reaching its working size against transient emit
buffers, most of which land on the large object heap and are then reused.

**Why it is accepted rather than fixed.** The fixed cost is Roslyn: 280 MB of the 402 is the
compilation, its semantic models and seven source generators, and it is there before the first
mutant. Making the per-mutant state smaller would not move a number that is already flat. If a much
larger solution ever shows a rising profile rather than a flat one, the measurement to repeat is this
one, and the thing to look at first is the compilation, not the mutants.

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

## RB-013 — The timeout budget is measured alone but spent under load · COVERED

The per-mutant budget is derived from a baseline run that happens with nothing else running, while
mutants are then tested with up to `--parallel` siblings competing for the machine. A healthy but slow
mutant could exceed its budget purely because of that contention and be recorded `Timeout` — counted
as a **detection**, so the effect is to *inflate* the score rather than to depress it. That is the
worst direction for an error to go in: the suite is credited with catching something it never
noticed.

**What contention actually costs.** Measured on a four-core machine: four concurrent runs of the
fixture's start-up-dominated suite took 0.444–0.514 s against 0.431–0.444 s alone — 18% worse at the
tail, which the default budget of three times the baseline plus thirty seconds absorbs many times
over. A CPU-bound suite has no such bound, though, because the test host parallelises internally as
well: the demand is workers times the host's own threads, against however many cores exist.

**The options, and why the chosen one is not a bigger number.** Scaling the factor by the worker
count, deriving the baseline under load, or simply widening the margin all make a false timeout less
likely without making it impossible, and each buys that by making every genuine endless loop slower
to detect. Re-running the timeouts once the workers have finished removes the cause instead: at that
point nothing else of ours is running, so a mutant that still exceeds its budget is slow on its own
merits. The cost is one extra run per timeout, and timeouts are rare — on a suite where they are not,
they are already the mutants dominating the run.

**Our tests.** `TimeoutConfirmationTests.A_timeout_that_does_not_reproduce_alone_is_not_believed`,
which injects a timeout into the first mutant exactly as contention would and requires the run to
reach that mutant's real verdict anyway; and
`A_timeout_that_does_reproduce_is_still_recorded`, so the confirmation cannot quietly turn a genuine
endless loop into a survivor.

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

---

## RB-016 — A mutation must not orphan a declaration · COVERED

Found on the very first run of KillMutants against its own source, which is the whole reason M10 did
that. It showed up twice, in two different disguises, before a single mutant had been tested.

**The underlying fact.** A pattern variable or an `out` variable is definitely assigned only
**conditionally** — "assigned when this expression is false" — and every mutation this tool makes to
such an expression changes when its parts are evaluated. This, which is ordinary C# and everywhere in
this codebase:

```csharp
if (node is not BinaryExpressionSyntax binary ||
    !Replacements.TryGetValue(binary.Kind(), out IReadOnlyList<SyntaxKind>? replacements))
{
    yield break;
}
```

mutated from `||` to `&&` leaves both `binary` and `replacements` unassigned at every later use:
`CS0165`. The same happens to a ternary — swapping the branches of
`d.TryGetValue(k, out var v) ? v : 0` moves `v` into the branch where it was never assigned. Sixteen
mutants in the first dogfood run were compile errors, every one of this shape.

**The second disguise, and the one that stopped the run.** The coverage probe erases the same state
for a different reason: `Hit(id, value)` returns its argument, so it cannot change what an expression
*evaluates to*, but conditional definite assignment does not survive being passed through a method
call. Wrapping that same `||` produced ten `CS0165`s across seven files, and the instrumented build
failed outright — no coverage, no run, no report.

**Why it was invisible until now.** The fixture code — comparisons, arithmetic, a ternary, a
null-coalesce — contains no pattern variables at all. The entire family sat outside what fixtures
could reach. Only real code has guard clauses.

**One rule, both symptoms.** An expression that declares a variable anywhere beneath it is not
mutated. That is checked once, at generation, in `MutationSite.DeclaresAVariable` — and because a
site is by definition a node some mutant replaces, a node that is never mutated is never instrumented
either. The instrumentation failure disappears as a consequence rather than needing a rule of its own.

The rule is deliberately blunt: any declaration beneath the node, even one whose scope could not
escape it. What it costs is the rare mutation of a declaration nothing reads afterwards; what it buys
is that neither failure can recur.

**Our tests.** `MutationSiteTests.An_expression_that_declares_a_variable_is_not_mutated`, which also
asserts that the ordinary expressions beside the guard are still mutated — a rule that swallowed the
file would pass a weaker test.

---

## RB-017 — The probe cannot accept every type a site can have · COVERED

The sibling of RB-016, and the reason its "sites are never instrumented if they are never mutated"
argument does not close the subject entirely.

**Why it matters.** The recorder is `T Hit<T>(int id, T value)`, and C# does not let every type be a
`T`. Verified against the .NET 10 SDK:

```
error CS9244: The type 'Span<int>' may not be a ref struct or a type parameter allowing ref
              structs in order to use it as parameter 'T'
```

A conditional expression is a mutation site, and `flag ? a : b` over two spans has exactly that type.
So this is reachable in ordinary code, and unlike RB-016 the mutation itself is perfectly valid — it
is only the *measurement* that cannot be expressed.

**Why the obvious repair is refused.** `where T : allows ref struct` fixes it in one word, and needs
C# 13. The probe is compiled into the **user's** project, whose language version we do not control;
[ADR-0007](adr/0007-measure-coverage-with-a-type-preserving-probe-en.md) keeps that source
deliberately conservative for exactly this reason. Buying coverage for spans at the price of refusing
to run on an older language version is the wrong trade.

**The rule.** A site whose value is a ref struct, a pointer, or `void` carries no recorder. Its
mutants are then tested against the whole suite, which is slower and never wrong. This is what makes
`CoverageMap.TestsReaching` answer three things rather than two: a list of tests, an empty list
(measured, nothing reaches it — `NoCoverage`), and `null` (not measured — run everything). Collapsing
the last two would report `NoCoverage` against code the tests do exercise.

**Our tests.** `MutationSitesTests.A_site_whose_value_is_a_ref_struct_carries_no_recorder`,
`An_ordinary_expression_still_carries_one`,
`Every_mutant_keeps_a_representative_and_every_site_lands_in_one_bucket`.

---

## RB-018 — A generator is rarely one file, and is not the developer's code · COVERED

Found by building the fixture this entry now rests on: a source generator with a helper assembly of
its own, referenced the way a packaged generator is. Two independent defects, either of which was
enough to make a perfectly ordinary project unusable.

**The generator's dependency did not load.** `AnalyzerLoader.AddDependencyLocation` did nothing, on
the stated grounds that "dependencies resolve from the analyzer's own directory, which the default
context already probes". Measured against the .NET 10 SDK: it does not.
`AssemblyLoadContext.Default` resolves a loaded assembly's dependencies through the *host's* probing
paths, not the loaded file's directory, so the generator threw `FileNotFoundException` during
initialisation. Roslyn reports that as `CS8784` — a **warning** — so the generator silently
contributed nothing and the project then failed to compile for want of the code it should have
produced, with an error that blamed KillMutants for a reconstruction that was in fact correct.
Mapperly, Refit and protobuf all ship helper assemblies, so this is the common shape rather than an
exotic one.

The fix records the directory of everything Roslyn registers and serves misses from there, through
`AssemblyLoadContext.Default.Resolving`. Hooking the fallback rather than loading eagerly is what
makes it safe: the event fires only after the normal search has failed, so an analyzer directory can
never win over the host's own `Microsoft.CodeAnalysis` and type identity across the boundary holds.

**The generator was being mutated.** A generator is referenced with `OutputItemType="Analyzer"` and
`ReferenceOutputAssembly="false"` — it runs inside the compiler at build time, and its assembly never
reaches the test project's output directory. Discovery followed the reference anyway, so the run
mutated the generator's own source. Every one of those mutants is uncoverable by construction: the
tests do not execute that code, and there is no assembly in the output directory to swap. Measured on
the fixture: ten of twelve mutants came from the generator, and now that uncovered mutants count
against the score (RB-012's sibling, and the reason this was worth finding), they dragged a project
with perfectly good tests from 100% to 16.67%. Project references are now followed only when they
contribute an assembly the tests will load.

**What this establishes about generator support, and what it does not.** A generator whose helper
assemblies sit beside it on the compiler's analyzer list now works. A generator built against a newer
Roslyn than the one KillMutants runs on still cannot be inspected — that is recorded in
`SourceGenerators.Unloadable` and reported by name rather than surfacing as an unexplained compile
error. A generator needing a *different version* of an assembly the host has already loaded will get
the host's: the `Resolving` fallback never fires for an assembly that resolved. That last one is
**accepted** rather than fixed. Loading analyzers into their own context would address it and would
require sharing the Roslyn assemblies across the boundary by hand, which is a great deal of machinery
for a case we have not yet seen.

**Our tests.** `MutationTestingEndToEndTests.A_source_generator_with_a_dependency_of_its_own_is_run_and_not_mutated`,
against `tests/fixtures/generator`, which fails on either defect alone.

---

## RB-019 — A pattern is made of constants, and a recorder is not one · COVERED

The one entry in this file that came from reading Stryker.NET rather than from running our own tool,
which is what the method in the header is for.

**How it was found.** Their orchestrator list carries a `ConstantPatternSyntaxOrchestrator` whose
whole body is "block injection here, restore it after". Nothing in it says why, so the question was
whether the constraint it defends against still exists for us — our instrumentation is a wrapping
call rather than an injected switch, so most of their placement rules do not apply. This one does.
Measured against the .NET 10 SDK: instrumenting the literal in `s is "abc"` yields
`CS9135 - a constant value of type 'string' is expected`, and the same for a switch expression arm.
The instrumented build fails, so the run stops before a single mutant is tested.

**The rule.** No site with a `PatternSyntax` or `SwitchLabelSyntax` ancestor carries a recorder. A
`when` clause is deliberately outside it: it is a sibling of the pattern rather than part of it, and
its expressions are ordinary code.

**What stays.** The *mutation* is unaffected and must be: `s is "abc"` rewritten to `s is ""` is a
constant, compiles, and changes what matches. This is a rule about recorders, exactly like RB-017 —
the mutants at those sites are tested against the whole suite instead of a measured subset.

**Our tests.** `MutationSitesTests.A_site_inside_a_pattern_carries_no_recorder`,
`A_site_in_a_when_clause_still_carries_one`, and the corpus entries "a literal in a constant pattern"
and "a literal in a switch expression arm", which assert both halves at once: the mutants compile and
differ, and instrumenting the file still leaves it building.


---

## RB-020 — A second run in one process reuses the first run's generators · OPEN

**How it was found.** Writing the test for RB-021, by accident. Two end-to-end tests in one process
run against the same generator fixture: the first adds a generator that throws, the second uses the
fixture unchanged. The second failed, deterministically, on three runs out of three — and it failed
because it was running the *first* test's generators.

**The mechanism.** `SourceGenerators.AnalyzerLoader` loads a project's generator assemblies with
`AssemblyLoadContext.Default.LoadFromAssemblyPath`. That context caches by assembly *identity*, not
by path, so the second call for a `Sample.Generator.dll` at a different path returns the assembly
already loaded from the first path — whatever it contains, and even when that path no longer exists.
The `Directories` list the `Resolving` fallback searches is `static` as well, so one run's analyzer
directories stay on the list for every run after it.

**What it costs.** Nothing through the CLI, which runs one session per process and exits. Through the
library API — `MutationTesting.RunAsync` called twice, which is what our own tests do and what a
watch mode or an IDE integration would do — a second run silently generates with the first run's
generators. The compilation is then not the project's, and every verdict measured against it
describes something else. That is the failure this tool exists not to produce, reached by a path the
shipped tool does not currently take.

**Why it is open rather than fixed.** The fix is the one RB-018 already declined: give the analyzers
their own `AssemblyLoadContext` and share the Roslyn assemblies across the boundary by hand. That is
real machinery, and it belongs in a change of its own rather than at the end of an unrelated one.
Until then, our own generator fixture renames its assembly when it carries a deliberately broken
generator, so the collision cannot reach another test.

**Our tests.** None yet. That is what OPEN means here.

---

## RB-021 — A generator that fails is a warning, and warnings do not stop a run · COVERED

**How it was found.** An automated review of the pull request that opened this repository pointed at
`SourceGenerators.Run` discarding the diagnostics `RunGeneratorsAndUpdateCompilation` returns. It was
right, and the reason it matters is the severity.

**What was measured.** Against Roslyn 5.9, a generator throwing from its initialiser is reported as
`CS8784`, severity **Warning**; one throwing while generating is `CS8785`, also a warning. In both
cases the generator contributes nothing and the compilation still emits. That is correct behaviour
for a compiler — the errors that follow point at the real problem — and it is the wrong behaviour to
inherit silently: RB-004 already relaxes warnings-as-errors, so nothing downstream would have
noticed.

**Why it is not merely a compile error.** When the missing code is required, the emit fails and the
run stops loudly; that case was never in doubt. The dangerous one is a generator whose output the
selected tests do not exercise: the assembly emits, the baseline passes, mutants are killed, and the
score describes an assembly the project does not build.

**The rule.** A generator run carries its failures out with it. Reconstructing the baseline, a
failure is fatal — everything the run measures is compared against that compilation. Emitting a
mutant, it is not: a mutation can genuinely break what a generator reads, so the mutant is reported
as one that could not be built, which the score leaves out and the run reports as untestable. An
error from a generator counts as a failure too: the project built before KillMutants touched it.

**Our tests.** `SourceGeneratorFailureTests`, which pins the two diagnostic ids and their severity
and proves a generator's own warnings are not treated as failures, and
`FailedGeneratorTests.A_run_stops_rather_than_reporting_on_a_compilation_a_generator_did_not_finish`,
which adds a throwing generator whose output nothing needs — so the build still works, and only the
new rule stops the run.

---

## RB-022 — Every emit builds a new generator driver · ACCEPTED

**How it was found.** The same automated review as RB-021 pointed out that
`SourceGenerators.Run` discards the driver `RunGeneratorsAndUpdateCompilation` returns — the one
holding Roslyn's incremental state — so a project with hundreds of mutants runs its generators from
cold hundreds of times instead of getting the cached subsequent runs the design documents.

**What was measured.** The claim is accurate about the mechanism and wrong about what it costs.
Against the .NET 10 SDK on `tests/fixtures/single`, which carries eight generators without asking for
any of them — `Microsoft.Interop.LibraryImportGenerator` and its siblings ship with the framework:

| | Cost |
| --- | --- |
| First generator run in the process | 1 139 ms |
| Every run after it | 4.5 ms |
| The whole emit around it | 9 ms |

The first run is assembly loading and JIT. That is paid once per process however the driver is held,
so it is not what driver reuse would save; the 4.5 ms is. Against our own last self-run — 499 mutants
in 7.4 minutes — reusing driver state could recover about two seconds, under one percent of the run,
and the mutant phase is dominated by launching test hosts rather than by anything Roslyn does.

**Why it is accepted rather than fixed.** A driver is state, and mutants are tested on several
workers at once. Buying under one percent of a run with state shared across those workers is a poor
trade in a tool whose worst failure is a verdict that is quietly wrong — and RB-020, on the same
page, is what sharing loader state across runs already cost us. This is recorded rather than done, so
that the next person to notice the discarded driver finds the measurement instead of repeating it.
