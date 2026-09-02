# DEC0005 | Verify the baseline through the mutation path before mutating

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

The most dangerous failure mode of a mutation testing tool is the **false kill**: the mutated
assembly fails the tests for a reason that has nothing to do with the mutation. A missing metadata
reference, a dropped generated source file, a wrong preprocessor symbol or a changed assembly version
all produce test failures that look exactly like a killed mutant.

This is not hypothetical. It was reproduced during this project's research: dropping the generated
`AssemblyInfo.cs` from the reconstructed compilation set the assembly version to `0.0.0.0`, the test
host then failed to load the assembly, and the resulting `FileNotFoundException` surfaced as an
ordinary test failure — reported as a kill.

A tool in that state reports a high mutation score and is silently worthless. Worse, it is
*reassuring*: nothing looks wrong.

A test run of the fixture costs about 0.6 s. Per-mutant timeouts, planned for M2, need a reference
duration to be derived from.

## Decision

In this context, we emit the unmutated compilation through exactly the same path a mutant takes, run
the tests on it before any mutant is considered, and abort the run with a diagnostic — reporting no
mutation results — when that run is not green.

## Rationale

Every class of compilation infidelity is caught at once, by construction, for the price of a single
test run. The reproduced `AssemblyInfo.cs` case is one instance of a family — missing reference,
dropped generated source, wrong preprocessor symbol, changed assembly version — and the check does
not need to know which member it is facing.

The check is meaningful precisely because it uses the mutation path rather than the pristine build
output: same command-line parse, same `CSharpCompilation`, same emit, same injection into the test
project's output directory. Verifying the pristine assembly would prove nothing about our emit.

Aborting rather than reporting is the only honest response to a red baseline, because the failure
mode this guards against is one where the numbers look good. A score computed on a broken foundation
is worse than no score.

## Alternatives considered

### Alternative 1 — Verify the pristine build output

* **Description:** run the tests against the assembly produced by the project's own build, before
  mutating.
* **Why rejected:** it would prove nothing about our emit. The infidelity being guarded against is
  introduced by the path KillMutants takes, which the pristine build never exercises.

## Consequences

### Positive

* Every class of compilation infidelity is caught at once, by construction, for the price of one test
  run (~0.6 s).
* It establishes the timing baseline that per-mutant timeouts will be derived from (M2).
* A user whose test suite is already red gets told so immediately, instead of receiving a mutation
  score computed on a broken foundation.

### Negative

* One extra test run per project. Negligible, and it is the single highest-value check in the tool.

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

*Not recorded at the time of the decision.*
