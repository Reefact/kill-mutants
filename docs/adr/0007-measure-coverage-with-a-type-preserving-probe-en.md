# ADR-0007 — Measure coverage with a type-preserving probe, one test at a time

**Status:** accepted · **Date:** 2026-08-31

## Context

Running every test for every mutant is the dominant cost of a run. Running only the tests that reach
a mutant needs a coverage map, and ADR-0002 leaves us with nothing that observes reachability: no
mutation switch is injected, so no code records having been executed.

Three ways to get it:

1. **Reuse a mutation switch as the probe**, as Stryker does — its `MutantControl.IsActive(id)` doubles
   as a coverage recorder. Unavailable to us, and reintroducing it would undo ADR-0002.
2. **External coverage tooling.** Gives line coverage, not per-test attribution, which is the part
   that matters.
3. **A dedicated coverage build with its own probe.**

And two ways to attribute a hit to a test: drive xUnit's `-automated sync` barrier, holding the host
between tests and reading the probe's output while it is blocked; or simply run one test at a time.

## Decision

A **coverage-only build** in which every mutation site is wrapped in a recorder that returns its
argument:

```csharp
public static T Hit<T>(int id, T value) { record(id); return value; }
```

and **one test run per test method**, selecting by name, with the recorder writing to a file private
to that run.

## Why this is not a return to schemata

The recorder is *type-preserving*. Wrapping an expression cannot change what it evaluates to, nor
when — short-circuiting, ordering and types all survive. That single property removes everything
that makes mutation switching hard: there is no branch to place, therefore no context in which the
placement is illegal, therefore no compile-and-roll-back loop. It is also never present at the same
time as a mutation: the coverage build is emitted once, used, and thrown away.

Verified on a mutation site inside an `Expression<Func<int, bool>>`, the case most likely to break a
rewriting scheme: it compiles, the recorder fires when the tree is compiled and invoked, and both
mutants there were killed.

## Why one run per test rather than a synchronisation barrier

The barrier is the cleverer mechanism and the wrong trade. One run per test needs no inter-process
communication, no protocol, and no reasoning about what else is in flight; attribution is exact
because nothing else is running. It reuses the name-based selection of ADR-0006 and the sandboxes of
milestone 6 unchanged, and parallelises across workers for free.

The cost is one process launch per test, paid once, against a mutant count that is normally an order
of magnitude larger.

## Consequences, measured

- **Uncovered mutants are never run at all**, and are reported `NoCoverage` rather than `Survived`.
  This is the unambiguous win, and it is as much about honesty as speed: "no test reaches this code"
  is a different and often more urgent finding than "a mutant survived".
- **Selection pays in proportion to how long the suite takes.** On a fixture whose tests do real
  work, 60 mutants went from 29.3 s to 22.6 s. On a fixture whose tests are instant, it saved
  nothing measurable.
- **Process startup is now the floor.** Each run costs roughly 0.5 s to launch a test host against
  0.12 s of testing. The obvious next lever is reusing a warm host, and we refuse it: that is the
  source of Stryker's longest-standing correctness complaint, where process-global state leaks
  between mutants and inflates scores.
- **A failed instrumented build stops the run** with a diagnostic, rather than silently degrading to
  running everything. `--no-coverage` is the escape hatch, and also what makes the two paths
  comparable in a test. The instrumented build is also required to *pass the suite* before anything
  is measured from it: wrapping cannot change what an expression evaluates to, but a coverage map
  built from a program that no longer behaves as it did would look perfectly valid.
- **Not every site can carry a recorder, and that is a third answer rather than a missing one.** A
  site whose value is a ref struct, a pointer, `void`, or has no natural type at all cannot be a
  `Hit<T>` argument (RB-017). Nor can a measurement that timed out, crashed or was cut short be read
  as "this test reaches nothing". Both cases resolve to *run every test*, which is slower and never
  wrong; only a site that was measured and found unreached is reported `NoCoverage`.

## Revisiting

If process startup ever stops dominating — a much slower suite, or many more tests than mutants —
the barrier-based attribution becomes worth its complexity. Nothing here forecloses it: it would
replace `CoverageCollector` and leave the map, the selection and the sandboxes untouched.
