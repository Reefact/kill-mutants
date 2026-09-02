# DEC0002 | One compilation per mutant

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

A mutation testing tool must produce, for each mutant, an assembly in which that one mutation is
active. There are three established ways to do it.

1. **One compilation per mutant.** Replace the syntax tree, emit a fresh assembly.
2. **Mutant schemata** (Stryker.NET's approach). Compile *all* mutants into one assembly, each
   guarded by an injected `MutantControl.IsActive(id)` check, and select the active one at run time
   through an environment variable or memory-mapped file.
3. **Source rewrite plus a real build.** Mutate the source file in a copy of the project and run
   `dotnet build`.

Schemata exists to avoid recompiling. What recompiling actually costs was measured on this platform
(SDK 10.0.111), on the milestone-1 fixture:

| Operation | Cost |
|---|---|
| Roslyn emit, reusing the `CSharpCompilation` | **6 ms / mutant** |
| `dotnet build` of the same project | ~1400 ms |
| One test-executable run | ~600 ms |

The compile-to-test ratio is about **1 : 100**. An independent study of Stryker.NET performed for
this project measured the same thing and reached the same conclusion. For 1,000 mutants that is
roughly 6 seconds of compilation against 10 minutes of test execution.

Emitting from a cold start costs ~1.6 s, almost all of it loading 167 metadata references — a
one-off per project, not per mutant.

One of the studies commissioned for this project argued the opposite case: adopt schemata from day
one, on the grounds that "source-rewrite-and-recompile per mutant is a dead end that cannot be
optimized later, only replaced", citing Stryker's own research notes. It also reports that Stryker's
warm-test-host reuse, which schemata makes attractive, requires explicit points at which those
long-lived processes are reset (`MicrosoftTestPlatformRunnerPool.cs:96,140`).

## Decision

In this context, we produce each mutant by its own Roslyn compilation and emit, with no schemata, no
injected control helper, no runtime activation channel, no placement-level analysis and no
compile/rollback loop.

## Rationale

At a compile-to-test ratio of 1:100, schemata removes about 1% of a run. That 1% is the entire
benefit, and it is bought with the largest source of complexity and correctness risk in the design —
a trade the measurement makes easy to refuse.

The correctness burden is what schemata actually costs. Whether a conditional can legally be
injected into a `const` initialiser, an attribute argument, an expression tree, a static constructor
or a pattern is a question that only exists because something is injected; one compilation per
mutant never asks it.

The claim that this path cannot be optimised later is not borne out by the measurements. The
expensive term is `N × tests`, and the two things that actually attack it — coverage-driven test
selection and parallelism — are indifferent to how the mutant reached the assembly. Both are easier
here, because each mutant is an isolated assembly in an isolated process.

Reasoning from Stryker's precedent rather than from measurement is what the dissenting study did.
Stryker's bet was made when the surrounding costs were different; the numbers above are from this
platform, today. A second study, which measured compile and test costs on this machine instead of
reasoning from precedent, reached this record's conclusion independently.

## Alternatives considered

### Alternative 1 — Mutant schemata

* **Description:** compile every mutant into a single assembly, each guarded by an injected
  `MutantControl.IsActive(id)` check, and select the active mutant at run time through an
  environment variable or a memory-mapped file. This is Stryker.NET's approach, and it was argued
  for by one of the studies commissioned for this project.
* **Why rejected:** it removes about 1% of a run, measured, in exchange for the design's largest
  correctness risk. Its claim to be the only optimisable path does not survive the measurement, and
  the warm-test-host reuse it makes attractive brings a class of reset bug that a process-per-mutant
  model cannot exhibit at all (DEC0008).

### Alternative 2 — Source rewrite plus a real build

* **Description:** mutate the source file in a copy of the project and run `dotnet build` for every
  mutant.
* **Why rejected:** `dotnet build` costs ~1400 ms against 6 ms for a Roslyn emit — more than 200
  times the cost of the path chosen, on the operation this alternative exists to perform.

## Consequences

### Positive

* Schemata's entire correctness burden disappears. There is no question of whether a conditional can
  legally be injected into a `const` initialiser, an attribute argument, an expression tree, a static
  constructor or a pattern — because nothing is injected.
* A failed emit is an unambiguous fact about exactly one mutant (`CompileError`), discovered directly
  instead of by recompiling up to 50 times and bisecting diagnostics.
* Each mutant is an independent assembly and an independent process, which makes the parallelism
  planned for M6 straightforward rather than a synchronisation problem.
* A mutant's assembly is exactly what it claims to be, which makes debugging a surprising result
  tractable.

### Negative

* We pay ~6 ms of compilation per mutant that schemata would avoid — roughly 1% of a run at a 1:100
  ratio.
* The `CSharpCompilation` must be kept alive and reused across mutants. Rebuilding it per mutant
  would invalidate this record's arithmetic.

### Risks

* On very large projects the per-emit cost may scale with project size rather than with the size of
  the change, taking compilation past the share of run time this decision assumes it occupies.

### Follow-up actions

* Reconsider this decision if profiling ever shows compilation exceeding ~20% of total run time.
