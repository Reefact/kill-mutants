# ADR-0005 — Verify the baseline through the mutation path before mutating

**Status:** accepted · **Date:** 2026-08-31

## Context

The most dangerous failure mode of a mutation testing tool is the **false kill**: the mutated
assembly fails the tests for a reason that has nothing to do with the mutation. A missing metadata
reference, a dropped generated source file, a wrong preprocessor symbol or a changed assembly
version all produce test failures that look exactly like a killed mutant.

This is not hypothetical. It was reproduced during this project's research: dropping the generated
`AssemblyInfo.cs` from the reconstructed compilation set the assembly version to `0.0.0.0`, the
test host then failed to load the assembly, and the resulting `FileNotFoundException` surfaced as an
ordinary test failure — reported as a kill.

A tool in that state reports a high mutation score and is silently worthless. Worse, it is
*reassuring*: nothing looks wrong.

## Decision

Before any mutant is considered, KillMutants **emits the unmutated compilation through exactly the
same path a mutant takes** — same command-line parse, same `CSharpCompilation`, same emit, same
injection into the test project's output directory — and runs the tests.

That run must be green. If it is not, the run aborts with a diagnostic saying the baseline failed,
and no mutation results are reported.

## Consequences

- Every class of compilation infidelity is caught at once, by construction, for the price of one
  test run (~0.6 s).
- The check is meaningful precisely because it uses the mutation path rather than the pristine build
  output. Verifying the pristine assembly would prove nothing about our emit.
- It also establishes the timing baseline that per-mutant timeouts will be derived from (M2).
- A user whose test suite is already red gets told so immediately, instead of receiving a mutation
  score computed on a broken foundation.
- One extra test run per project. Negligible, and it is the single highest-value check in the tool.
