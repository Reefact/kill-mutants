# ADR-0002 — One compilation per mutant

**Status:** accepted · **Date:** 2026-08-31

## Context

A mutation testing tool must produce, for each mutant, an assembly in which that one mutation is
active. There are three established ways to do it.

1. **One compilation per mutant.** Replace the syntax tree, emit a fresh assembly.
2. **Mutant schemata** (Stryker.NET's approach). Compile *all* mutants into one assembly, each
   guarded by an injected `MutantControl.IsActive(id)` check, and select the active one at run time
   through an environment variable or memory-mapped file.
3. **Source rewrite plus a real build.** Mutate the source file in a copy of the project and run
   `dotnet build`.

Schemata exists to avoid recompiling. The question is what recompiling actually costs.

## Measurements

Measured on this platform (SDK 10.0.111), on the milestone-1 fixture:

| Operation | Cost |
|---|---|
| Roslyn emit, reusing the `CSharpCompilation` | **6 ms / mutant** |
| `dotnet build` of the same project | ~1400 ms |
| One test-executable run | ~600 ms |

The compile-to-test ratio is about **1 : 100**. An independent study of Stryker.NET performed for
this project measured the same thing and reached the same conclusion.

For 1,000 mutants that is roughly 6 seconds of compilation against 10 minutes of test execution.

## Decision

**One Roslyn compilation and emit per mutant.** No schemata, no injected control helper, no runtime
activation channel, no placement-level analysis, no compile/rollback loop.

## Consequences

Positive, and the reason for the decision:

- Schemata's entire correctness burden disappears. There is no question of whether a conditional
  can legally be injected into a `const` initialiser, an attribute argument, an expression tree, a
  static constructor or a pattern — because nothing is injected.
- A failed emit is an unambiguous fact about exactly one mutant (`CompileError`), discovered
  directly instead of by recompiling up to 50 times and bisecting diagnostics.
- Each mutant is an independent assembly and an independent process, which makes the parallelism
  planned for M6 straightforward rather than a synchronisation problem.
- A mutant's assembly is exactly what it claims to be, which makes debugging a surprising result
  tractable.

Negative, and accepted:

- We pay ~6 ms of compilation per mutant that schemata would avoid. At a 1:100 ratio this is
  roughly 1% of the run, in exchange for removing the largest source of complexity and correctness
  risk in the design.
- The `CSharpCompilation` must be kept alive and reused across mutants. Emitting from a cold start
  costs ~1.6 s, almost all of it loading 167 metadata references — a one-off per project, not per
  mutant. Rebuilding it per mutant would invalidate this ADR's arithmetic.

## Revisiting

This decision should be reconsidered if profiling ever shows compilation exceeding ~20% of total
run time — for example on very large projects where the per-emit cost scales with project size
rather than with the size of the change.
