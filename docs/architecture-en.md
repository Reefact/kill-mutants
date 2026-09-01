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
   green. This is not optional — see [ADR-0005](adr/0005-verify-the-baseline-before-mutating-en.md).
4. **Generate** mutants by walking the syntax trees with the mutator catalog.
5. For each mutant: replace the syntax tree, emit, write the assembly into the test project's
   output directory, run the test executable, classify the outcome, restore the original.
6. **Report** the counts and the mutation score.

## 4. How the conceptual concerns map onto code

The concerns below are kept distinct as namespaces and types. They are deliberately **not** kept
distinct as assemblies: for milestone 1 that would be thirteen projects for a few hundred lines,
which is the premature structure this project set out to avoid. Splitting later is cheap; the
namespace boundaries are already where the assembly boundaries would go.

| Concern | Namespace | Notes |
|---|---|---|
| Project discovery | `KillMutants.Projects` | Locates the project under test and its test project |
| Code analysis | `KillMutants.Analysis` | `csc` command line -> `CSharpCompilation` |
| Mutation generation | `KillMutants.Mutations` | Walks trees, produces candidates |
| Mutator catalog | `KillMutants.Mutations.Mutators` | `IMutator` and its implementations |
| Mutant representation | `KillMutants.Mutations` | `Mutant`, `MutantId`, `MutantStatus`, `SourceLocation` |
| Instrumentation | *(collapsed)* | See below |
| Compilation | `KillMutants.Compilation` | Emits a mutated assembly |
| Test discovery | `KillMutants.Testing` | `-list tests /json`, by name (ADR-0006) |
| Test execution | `KillMutants.Testing` | `ITestRunner`, `TestRunOutcome` |
| xUnit 4 / MTP 2 specifics | `KillMutants.Testing.XUnit` | The only place that knows the runner's CLI |
| Test-to-mutant mapping | `KillMutants.Coverage` | A type-preserving probe, one run per test (ADR-0007) |
| Orchestration | `KillMutants.Execution` | The short, linear phase list |
| Results | `KillMutants.Reporting` | `MutationTestReport`, console writer |
| CLI | `KillMutants.Cli` | `dotnet killmutants` |

**Instrumentation has no code of its own, by design.** Because each mutant gets its own compilation
([ADR-0002](adr/0002-one-compilation-per-mutant-en.md)), "instrumenting" a mutant is one call to
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
- `MutantStatus` — `Killed`, `Survived`, `CompileError`, `Timeout`, `NoCoverage`, `Pending`.
  Milestone 1 only produces `Killed` and `Survived`, but the vocabulary is fixed now so that later
  milestones add behaviour rather than reshape the model.
- `MutationScore` — a value type that knows how to compute and render itself, so no caller ever
  divides two integers and formats a percentage by hand.

## 6. Risks

Ordered by expected damage, from the study and from our own probes.

**Critical — false kills from compilation infidelity.** If the reconstructed compilation differs
from the real build in any way (a missing generated `AssemblyInfo.cs` changing the assembly version,
a missing reference, a wrong preprocessor symbol), the tests fail for reasons unrelated to the
mutation and every mutant is reported `Killed`. A mutation tool that always says "Killed" is worse
than no tool, because it is silently reassuring. *Mitigated by ADR-0005: the baseline is emitted
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

**Medium — mutations that introduce an infinite loop.** Not reachable by milestone 1's single
mutator, but the timeout must exist before the catalog grows. *Deferred to M2, with the baseline
duration already recorded for the budget.*

## 7. Roadmap position

Milestone 1 was one project pair, one mutator (`>=` becomes `>`), one mutant, executed for real.
M2 grew the catalogue to six families. M3 handles real solution structure: several test projects,
several projects under test, project references followed transitively, and a framework pinned per
project. M6 tests mutants in parallel, each worker in a private copy of the test output directory. M4 and M5
discover the tests and measure which ones reach which mutants, so uncovered mutants are never run and
the rest run only what can kill them. Still ahead: M7 reporting; M8 CI; M9 advanced mutations.

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
  emit at one thread, 0.85 ms at four — which strengthens ADR-0002 rather than straining it: the
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
