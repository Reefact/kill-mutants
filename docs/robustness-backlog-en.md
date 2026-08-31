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

## RB-002 — Source generators contribute code that is not on the source list · OPEN

**What Stryker does.** Runs the generator driver, and re-runs it after every `ReplaceSyntaxTree`
(`Compiling/CsharpCompilingProcess.cs:360-364`). It also detects projects pinning a newer compiler
than its own and logs it by name (`ReferencesNewerCompiler`,
`Buildalyzer/IAnalyzerResultExtensions.cs:115-120`).

**Why it exists.** `CscCommandLineArgs` lists generators under `/analyzer:` but does **not** list
their output among the sources — the compiler produces it during the build.

**Does it still apply?** Yes, and more than before: `[GeneratedRegex]`, `[JsonSerializable]`,
`[LibraryImport]`, Mapperly, Refit and ASP.NET Core minimal APIs are all generator-based. Reproduced:
a project with a `[GeneratedRegex]` partial property fails to emit with `CS9248` when generators are
not run. Our own fixture's command line already carries eight `/analyzer:` assemblies.

**Impact.** ADR-0003's claim that generated sources "come along automatically" is true only for
SDK-emitted files. ADR-0005 makes this fail *safely* — the baseline goes red rather than producing
false kills — but KillMutants simply does not work on such projects today.

**What we must do.** Run `CSharpGeneratorDriver` over the compilation. Two subtleties to decide
deliberately: generator output can depend on the mutated tree, so strictly the driver must re-run per
mutant (running once on the pristine compilation is a defensible approximation for operator-level
mutators, and must be documented as one); and generators load into our own process, so a project
pinning a newer Roslyn than ours silently contributes nothing — that case must be reported by name,
not surfaced as "KillMutants could not compile your project".

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

## RB-006 — Injection is not crash-safe · OPEN

If KillMutants is killed abnormally (CI cancellation, SIGKILL) while a mutant is injected,
`AssemblyInjection.Dispose` never runs and the developer is left with a mutated assembly and a
`.killmutants-original` file in `bin`. Stryker has the same wound and only logs it
(`ProjectComponents/TestProjects/TestProjectsInfo.cs:51-58`).

**What we should do.** Detect a leftover backup on startup and restore it before doing anything else.
Cheap, and it turns a confusing failure into a non-event.

---

## RB-007 — A multi-targeted project yields one command line per framework · OPEN

`MsBuildQuery` asks for `CscCommandLineArgs` without pinning a target framework. On a project with
several, the result is ambiguous, and mutants could be emitted against a framework the test project
does not use. Must be resolved before M3.

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

## RB-010 — A mutation can turn a terminating loop into an endless one · PARTIAL

The budget is computed from the baseline duration and `ProcessRunner` kills the whole process tree on
expiry, but **no test yet proves an endlessly looping mutant is caught end to end**. Until one
exists, `Timeout` is a status produced by code we have not watched work. Closing this is part of the
catalogue milestone, since a loop-condition mutator is what makes it reachable.

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
