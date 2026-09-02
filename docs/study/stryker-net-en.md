# Study — Stryker.NET, and what KillMutants takes from it

Stryker.NET (Apache-2.0, <https://github.com/stryker-mutator/stryker-net>) is the reference
implementation of mutation testing for .NET. It was studied to understand the *problem*, not to
be copied. No Stryker source code has been reused in KillMutants; what follows is a description
in our own words, with `file:line` citations so any claim can be checked against the original.

Studied at commit of 2026-08-31, against the `master` branch.

## 1. Shape of the codebase

598 `.cs` files (390 production, 208 unit test), ~27k production lines, 11 production projects.
The part that actually performs mutation testing is small: roughly seven files and ~1,200 lines,
in a short linear chain.

    StrykerRunner -> ProjectOrchestrator -> InitialisationProcess -> ProjectMutator
                  -> MutationTestProcess -> MutationTestExecutor -> ITestRunner

Everything else is breadth, and almost all of that breadth is compatibility:

| Area | Size | Why it exists |
|---|---|---|
| Options / configuration | ~21% of all files (49 one-class-per-option types + 50 tests) | Ten years of accumulated switches |
| VSTest + DataCollector | ~2,200 lines | Pre-MTP test platform, .NET Framework hosts |
| Buildalyzer project discovery | ~2,500 lines | .NET Framework, multi-TFM, `packages.config`, non-SDK projects |
| Reporters, baseline, dashboard | ~2,900 lines | HTML/JSON/dashboard reporting, S3/Azure baselines |

The lesson we keep is structural, not mechanical: Stryker's top-level phase list
(`StrykerRunner.cs:49-164`) stayed short, linear and readable through a decade of feature growth.
That shape is worth imitating. The lesson we avoid: `Stryker.Core` references both concrete test
runners and selects between them in a `switch` (`Initialisation/ProjectOrchestrator.cs:72-78`), so
the `ITestRunner` abstraction buys nothing at the composition root.

## 2. The mutation engine

Mutators are small classes implementing a common mutator interface, each declaring the syntax node
kind it handles and returning zero or more mutations. Binary comparison operators — precisely what
milestone 1 needs — are handled in `Mutators/BinaryExpressionMutator.cs:33`, which maps
`GreaterThanOrEqualExpression` to both `<` and `>`.

Orchestration walks the syntax tree with node-kind-specific orchestrators rather than one
`CSharpSyntaxRewriter`, because Stryker must also track *where a mutation may legally be placed*
(see §3). A narrow tool that mutates one node at a time does not need that machinery: a plain
`CSharpSyntaxWalker` to find candidates, and `SyntaxNode.ReplaceNode` to apply one, is sufficient.

## 3. Instrumentation — mutant schemata, and why we do not need it

This is Stryker's single most consequential design decision, and the one KillMutants deliberately
does not follow.

Stryker compiles **every mutant of a project into one assembly**. Each mutation is emitted as an
extra branch beside the original code, guarded by an injected call:

- statements become `if (MutantControl.IsActive(n)) { mutated } else { original }`
- expressions become `(MutantControl.IsActive(n) ? mutated : original)`

At run time one mutant is selected through a side channel — an environment variable for the VSTest
path, a memory-mapped file for the MTP path (`MicrosoftTestingPlatformRunner.cs:129-180`).

Placement is purely syntactic; the semantic model is never consulted at injection time. A stack of
"mutation control levels" (MemberAccess < Expression < Statement < Block < Member) lets a mutation
that cannot be hosted at its own level float upward. Mutations that reach the top are dropped.

Because the compiler is the only thing that can actually decide whether an injected mutant is
legal, Stryker needs a **rollback loop**: a 394-line process that recompiles up to 50 times,
removing the mutants whose injected branches broke the build. Supporting this are eight reversible
"instrumentation engines" and `SyntaxAnnotation` bookkeeping.

The cost this pays for is compilation. We measured that cost on the target platform and it is not
worth paying — see [DEC0002](../decisions/0002-one-compilation-per-mutant-en.md). Compiling one mutant per
assembly makes every one of these mechanisms unnecessary: schemata, `MutantControl`, the random
helper namespace, the control-level stack, the annotation bookkeeping, the rollback loop, and the
runtime activation channel all disappear. A failed emit becomes an unambiguous fact about one
mutant rather than a search problem.

## 4. Compilation

Stryker does not reuse MSBuild's compiler output. It runs a Buildalyzer design-time build and then
hand-reconstructs `CSharpCompilationOptions` and `CSharpParseOptions` from raw MSBuild property
strings (`IAnalyzerResultCSharpExtensions.cs:16-108`): output kind, `AllowUnsafeBlocks`,
`CheckForOverflowUnderflow`, nullable context, `NoWarn`/`WarningsAsErrors` merging, warning level,
`LangVersion` parsing, and string surgery over the `Features` property.

This reconstruction is the largest source of accidental complexity in the codebase. Around it sit
a Mono.Cecil-based embedded-resource recovery subsystem, a hand-rolled analyzer-config options
provider, a custom analyzer assembly loader, and a workaround for a long-fixed Roslyn bug.

On .NET 10 none of it is necessary, because MSBuild will simply hand over the exact `csc` command
line and Roslyn will parse it. That is [DEC0003](../decisions/0003-compilation-inputs-from-csc-command-line-en.md).

The one thing Stryker gets exactly right here, and which we copy as a *decision* rather than as
code, is where the mutant goes: the mutated bytes are written over the source project's assembly
**inside the test project's output directory** (`ProjectComponents/TestProjects/TestProjectsInfo.cs:87`),
with the original moved aside first. Nothing in the test project's references is rewritten, because
the runtime loads whatever assembly sits next to the test assembly.

## 5. Test execution

Stryker supports two runners behind one abstraction: VSTest (`Stryker.TestRunner.VsTest`, 11 files,
~1,872 lines, plus a separate `netstandard2.0` data collector) and Microsoft Testing Platform
(`Stryker.TestRunner.MicrosoftTestPlatform`, with an `RPC/` folder implementing a JSON-RPC client
against MTP's server mode). KillMutants supports only MTP-based projects, so the entire VSTest arm,
the data collector, and the abstraction that exists to let the two coexist are all out of scope.

Stryker's MTP runner starts each test assembly as a long-lived `--server --client-port N` process,
with Stryker as the TCP listener and the test application dialling back, then speaks
`Content-Length`-framed JSON-RPC: `initialize`, `testing/discoverTests`, `testing/runTests`, `exit`,
with streamed `testing/testUpdates/tests` notifications terminated by a `changes: null` sentinel.

Two findings made this worth not copying. First, in server mode the host **always exits 0**
regardless of test failures, so a server-mode client must interpret the streamed nodes rather than
the exit code. Second, and more usefully, MTP 2 has gained two capabilities that Stryker's
server-mode design predates and does not use: `--list-tests json` for machine-readable discovery,
and platform-level `--filter-uid` to run exactly a named set of test UIDs. Together they provide
discovery and per-test selection — the two things M4 and M5 need — **with no RPC code at all**,
which is why [DEC0004](../decisions/0004-run-tests-by-launching-the-test-executable-en.md) does not treat
server mode as inevitable.

## 6. Coverage and test-to-mutant mapping

Stryker uses no coverage tool at all: it reuses the mutation-switching instrumentation as the
coverage probe. Every mutation site is already guarded by `MutantControl.IsActive(id)`, so in
capture mode that call registers the id and returns false, and one extra run reveals which mutants
are reachable.

Attributing *which test* reached a mutant is then plumbing, and the plumbing is where the cost
lands. On VSTest an in-process data collector snapshots and resets the list at `TestCaseStart` /
`TestCaseEnd`. MTP has no data collector equivalent, so Stryker falls back to environment variables
plus memory-mapped files plus a polling "epoch relay" handshake, running literally one test per RPC
request. Static constructors and initialisers are the entire source of the remaining complexity,
because they run once per process and cannot be attributed to a single test.

The useful discovery for M5 is that xUnit 4 offers a much simpler primitive: `-automated sync` is a
hard, race-free per-test barrier — the host blocks until it reads a newline — which collapses
collector, memory-mapped file and epoch handshake into "read message, act while the host is blocked,
write newline". KillMutants needs none of this for M1, but it now knows what M5 should be built on.

Note also that our design does not need the coverage probe to be an *injected* one: with one
assembly per mutant, reachability can be established from the baseline run rather than from
instrumentation that must survive into production code.

## 7. Performance

The naive cost is `N mutants x compile x full test suite`. Stryker attacks the first factor
(schemata: one compilation for all mutants), the second (coverage-based test selection, bail out on
the first failing test), and the third (parallel test hosts, and a timeout derived from the
baseline run so that a mutation which introduces an infinite loop does not hang the run).

Our own measurements say the first factor is the wrong one to attack on modern .NET — see DEC0002.
Test execution dominates by two orders of magnitude, so test *selection* and *parallelism* are where
the wins are, and both are additive to our design rather than requiring it to change.

Three details from this part of the study are worth carrying forward:

- **Bail-out on the first failing test is implemented only for Stryker's legacy VSTest runner and is
  explicitly absent from its MTP runner** (open issue #3655). KillMutants gets it for free from the
  xUnit console runner's `-stopOnFail`, and already uses it for mutant runs.
- **Warm test-host reuse needs explicit reset points.** Stryker returns runners to a pool after each
  piece of work (`VsTestRunnerPool.cs:95-111`) and must force those long-lived processes back to a
  clean state at phase boundaries (`MicrosoftTestPlatformRunnerPool.cs:96,140`). That discipline is
  a direct argument for our process-per-mutant model, which needs none of it - see DEC0008.
- **MTP's own `--timeout` does not reliably stop a spinning test.** The timeout must be owned by the
  tool, with `Process.Kill(entireProcessTree: true)` on expiry. KillMutants does exactly that.

## 8. What we concluded

**Intrinsic to any mutation testing tool** — acquire the compilation inputs of the project under
test; know which test project exercises it and where its output directory is; build once; establish
a green baseline and a timing baseline; parse, mutate and emit; place the mutated assembly where the
test host loads it, and restore the original afterwards; classify the outcome; enforce a timeout;
report and exit with a meaningful code.

**Present only for history** — VSTest and its data collector; the environment-variable activation
channel; `packages.config` and NuGet restore fallbacks; msbuild.exe discovery via vswhere; .NET
Framework guards; multi-TFM disambiguation; solution `Configuration|Platform` matching; the
`Language` enum and generic orchestrator shape left over from planned VB/F# support; Mono.Cecil,
used only to read two assembly attributes for the dashboard reporter.

**Radically simplifiable on our constraints** — project analysis (Buildalyzer to a single MSBuild
call), option reconstruction (to one Roslyn call), schemata and rollback (to nothing), the options
framework (49 classes to one record), reporting (11 reporters to one console writer), and mutant
filtering (13 files to none, for now).

**The major risks we inherited from this study** are recorded in
[architecture-en.md](../architecture-en.md#6-risks), the most serious being *false kills* caused by an
infidelity in the reconstructed compilation rather than by the mutation itself.
