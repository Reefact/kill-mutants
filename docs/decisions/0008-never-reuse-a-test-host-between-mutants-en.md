# DEC0008 | Never reuse a test host between mutants

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

With coverage-driven selection in place (DEC0007), a mutant's test run costs roughly **0.5 s to
launch a test host against 0.12 s of actually testing**. Startup is the floor, and it is now the
largest single cost in a run. The obvious way to remove it is to keep test hosts alive and hand each
mutant to an already-running one.

Stryker.NET does exactly that. Both its runner pools keep a bag of live runners, take one for a piece
of work and return it afterwards (`VsTestRunnerPool.cs:95-111`), and it needs explicit points at
which those long-lived processes are forced back to a clean state — after the initial test run, and
after the coverage run, where the comment records the second reason plainly: *"Reset test processes
to trigger coverage file flush (process exit writes coverage)"*
(`MicrosoftTestPlatformRunnerPool.cs:96,140`).

Reuse is not an independent optimisation there. Stryker can reuse a host because, under mutant
schemata, the assembly on disk never changes: every mutant is already compiled into it and a runtime
switch selects which one is live. Handing a warm process a different mutant is just setting a
variable.

KillMutants changes the assembly itself for every mutant (DEC0002), and a .NET process does not
re-read an assembly it has already loaded.

Three options follow:

1. **A fresh process per mutant.** Pays the launch cost.
2. **Adopt schemata and reuse hosts.**
3. **One host, a collectible `AssemblyLoadContext` per mutant.** Keeps one assembly per mutant, but
   moves isolation from the operating system to the CLR: static state outside the collectible context
   still persists, an unloadable context that fails to unload leaks, and any test touching a type
   from the default context defeats it.

An earlier study for this project attributed a score-inflating state-leak bug in Stryker to issue
#3742. That reference could not be reached and is **unverified**; nothing here rests on it.
Everything above rests on the code cited, which was read directly, and on the mechanism, which needs
no citation: a process that is not restarted keeps whatever the last test put in it.

## Decision

In this context, we launch a fresh test host process for every mutant run, and never pool, reuse or
keep a test host warm.

## Rationale

A warm host cannot be correct here. Because a .NET process does not re-read an assembly it has
already loaded, and because each mutant is a different assembly on disk, a reused host would keep
testing the mutant it started with and report every subsequent one as survived — a silent, total
corruption of the result.

So the real choice is not "fast or careful". Every way of keeping the host warm either reverses
DEC0002 or moves isolation into the CLR, where it depends on nothing in the suite under test
touching the default context.

The trade is not close, because KillMutants' entire output is one claim: *these mutants were caught,
these were not*. A mechanism that lets one mutant's state colour another's verdict does not make the
tool slightly less accurate — it makes the number it prints untrustworthy, in a direction that
flatters. The failure would also be invisible: a mutant wrongly reported killed looks exactly like a
mutant genuinely killed. Speed is a feature; the score is the product.

Paying the launch cost buys isolation guaranteed by the operating system rather than by remembering
to reset — the very thing Stryker's explicit reset points exist to compensate for.

## Alternatives considered

### Alternative 1 — Adopt schemata and reuse hosts

* **Description:** compile every mutant into one assembly behind a runtime switch, as Stryker does,
  which makes handing a warm process a different mutant a matter of setting a variable.
* **Why rejected:** it reverses DEC0002 and takes on everything that decision removed — conditional
  placement, illegal contexts, the compile-and-roll-back loop — plus the state leakage that makes
  explicit reset points necessary in the first place.

### Alternative 2 — One host, a collectible `AssemblyLoadContext` per mutant

* **Description:** keep a single process and load each mutant's assembly into its own collectible
  context, unloading it between mutants.
* **Why rejected:** it moves isolation from the operating system to the CLR. Static state outside the
  collectible context still persists, a context that fails to unload leaks, and any test touching a
  type from the default context defeats it.

## Consequences

### Positive

* Isolation is guaranteed by the operating system rather than by remembering to reset. No static
  field, cache, singleton, loaded assembly, culture setting or open handle can carry from one mutant
  to the next, because there is no "next" — there is only a new process.
* It is what makes milestone 6's parallelism safe: sandboxed output directories plus
  process-per-mutant means two concurrent mutants have no shared surface at all.
* It composes with DEC0002 rather than fighting it. One mutant, one assembly, one process, one
  verdict, all the way down.

### Negative

* We keep a floor of roughly 0.5 s per mutant run that Stryker does not pay.

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

* Revisit the collectible `AssemblyLoadContext` option — the only future path that does not reverse
  DEC0002 — if process startup stops being the floor for another reason, such as a much slower suite
  or many more tests than mutants. Adopting it would need evidence that a collectible context
  genuinely isolates a real test suite, not just a fixture.
