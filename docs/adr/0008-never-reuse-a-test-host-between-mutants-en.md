# ADR-0008 — Never reuse a test host between mutants

**Status:** accepted · **Date:** 2026-08-31

## Context

With coverage-driven selection in place (ADR-0007), a mutant's test run costs roughly **0.5 s to
launch a test host against 0.12 s of actually testing**. Startup is the floor, and it is now the
largest single cost in a run. The obvious way to remove it is to keep test hosts alive and hand each
mutant to an already-running one.

Stryker.NET does exactly that. Both its runner pools keep a bag of live runners, take one for a piece
of work and return it afterwards (`VsTestRunnerPool.cs:95-111`), and it needs explicit points at
which those long-lived processes are forced back to a clean state — after the initial test run, and
after the coverage run, where the comment records the second reason plainly: *"Reset test processes
to trigger coverage file flush (process exit writes coverage)"*
(`MicrosoftTestPlatformRunnerPool.cs:96,140`).

## The part that decides it

Reuse is not an independent optimisation. It is **coupled to mutant schemata**.

Stryker can reuse a host because, under schemata, the assembly on disk never changes: every mutant is
already compiled into it and a runtime switch selects which one is live. Handing a warm process a
different mutant is just setting a variable.

KillMutants changes the assembly itself for every mutant (ADR-0002). A .NET process does not re-read
an assembly it has already loaded, so a warm host would keep testing the mutant it started with and
report every subsequent one as survived — a silent, total corruption of the result.

So the real choice is not "fast or careful". It is:

1. **A fresh process per mutant.** Pays the launch cost.
2. **Adopt schemata and reuse hosts.** Reverses ADR-0002 and takes on everything it removed:
   conditional placement, illegal contexts, the compile-and-roll-back loop — plus the state leakage
   that makes explicit reset points necessary.
3. **One host, a collectible `AssemblyLoadContext` per mutant.** Keeps one assembly per mutant, but
   moves isolation from the operating system to the CLR: static state outside the collectible context
   still persists, an unloadable context that fails to unload leaks, and any test touching a type
   from the default context defeats it.

## Decision

**A fresh test host process per mutant run. Test hosts are never pooled, reused, or kept warm.**

## Consequences

- We keep a floor of roughly 0.5 s per mutant run that Stryker does not pay.
- In exchange, isolation is guaranteed by the operating system rather than by remembering to reset.
  No static field, cache, singleton, loaded assembly, culture setting or open handle can carry from
  one mutant to the next, because there is no "next" — there is only a new process.
- It is what makes milestone 6's parallelism safe: sandboxed output directories plus process-per-
  mutant means two concurrent mutants have no shared surface at all.
- It composes with ADR-0002 rather than fighting it. One mutant, one assembly, one process, one
  verdict, all the way down.

## Why the trade is not close, for this tool

KillMutants' entire output is one claim: *these mutants were caught, these were not*. A mechanism
that lets one mutant's state colour another's verdict does not make the tool slightly less accurate —
it makes the number it prints untrustworthy, in a direction that flatters. Nothing else the tool does
matters if the score can be silently inflated, and the failure would be invisible: a mutant wrongly
reported killed looks exactly like a mutant genuinely killed.

Speed is a feature. The score is the product.

## Revisiting

Option 3 is the only future path that does not reverse ADR-0002, and it becomes worth measuring only
if process startup stops being the floor for another reason — a much slower suite, or many more tests
than mutants. Adopting it would need evidence that a collectible context genuinely isolates a real
test suite, not just a fixture.

## A note on sourcing

An earlier study for this project attributed a score-inflating state-leak bug in Stryker to issue
#3742. This session cannot reach the GitHub API for that repository, so that reference is
**unverified** and this ADR does not rest on it. Everything above rests on the code cited, which was
read directly, and on the mechanism, which needs no citation: a process that is not restarted keeps
whatever the last test put in it.
