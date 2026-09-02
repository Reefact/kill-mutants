# DEC0007 | Measure coverage with a type-preserving probe, one test at a time

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

Running every test for every mutant is the dominant cost of a run. Running only the tests that reach
a mutant needs a coverage map, and DEC0002 leaves us with nothing that observes reachability: no
mutation switch is injected, so no code records having been executed.

Three ways to get it:

1. **Reuse a mutation switch as the probe**, as Stryker does — its `MutantControl.IsActive(id)`
   doubles as a coverage recorder.
2. **External coverage tooling.**
3. **A dedicated coverage build with its own probe.**

And two ways to attribute a hit to a test: drive xUnit's `-automated sync` barrier, holding the host
between tests and reading the probe's output while it is blocked; or simply run one test at a time.

Measured on the fixtures: each test-host launch costs roughly 0.5 s against 0.12 s of testing. On a
fixture whose tests do real work, 60 mutants went from 29.3 s to 22.6 s with selection; on a fixture
whose tests are instant, selection saved nothing measurable. A run normally has an order of magnitude
more mutants than tests.

Stryker's longest-standing correctness complaint concerns warm test-host reuse, where process-global
state leaks between mutants and inflates scores.

Some mutation sites have no type that can be passed through a generic wrapper: a value that is a ref
struct, a pointer, `void`, or has no natural type at all (RB-017). A measurement can also time out,
crash, or be cut short.

## Decision

In this context, we measure coverage from a dedicated coverage-only build in which every mutation
site is wrapped in a type-preserving recorder, running one test method at a time selected by name,
with the recorder writing to a file private to that run.

## Rationale

The recorder is *type-preserving*:

```csharp
public static T Hit<T>(int id, T value) { record(id); return value; }
```

Wrapping an expression cannot change what it evaluates to, nor when — short-circuiting, ordering and
types all survive. That single property removes everything that makes mutation switching hard: there
is no branch to place, therefore no context in which the placement is illegal, therefore no
compile-and-roll-back loop. It is also never present at the same time as a mutation: the coverage
build is emitted once, used, and thrown away. So this is not a return to schemata, and DEC0002
stands.

It was verified on a mutation site inside an `Expression<Func<int, bool>>`, the case most likely to
break a rewriting scheme: it compiles, the recorder fires when the tree is compiled and invoked, and
both mutants there were killed.

One run per test rather than the barrier is the right trade even though the barrier is the cleverer
mechanism. One run per test needs no inter-process communication, no protocol, and no reasoning about
what else is in flight; attribution is exact because nothing else is running. It reuses the
name-based selection of DEC0006 and the sandboxes of milestone 6 unchanged, and parallelises across
workers for free. Its cost is one process launch per test, paid once, against a mutant count that is
normally an order of magnitude larger.

## Alternatives considered

### Alternative 1 — Reuse a mutation switch as the probe, as Stryker does

* **Description:** let the injected `MutantControl.IsActive(id)` check double as a coverage recorder.
* **Why rejected:** it is unavailable to us — DEC0002 injects no mutation switch — and reintroducing
  one would undo that decision.

### Alternative 2 — External coverage tooling

* **Description:** obtain coverage from an existing tool rather than instrumenting ourselves.
* **Why rejected:** it gives line coverage, not per-test attribution, which is the part that matters
  for selecting the tests that reach a mutant.

### Alternative 3 — Attribute hits through a synchronisation barrier

* **Description:** drive xUnit's `-automated sync` barrier, holding the test host between tests and
  reading the probe's output while it is blocked.
* **Why rejected:** it is the cleverer mechanism and the wrong trade. It requires inter-process
  communication, a protocol, and reasoning about what else is in flight, to save process launches
  that are paid once against a mutant count an order of magnitude larger.

### Alternative 4 — Reuse a warm test host across runs

* **Description:** the obvious next lever once process startup becomes the floor: keep the test host
  alive between runs instead of launching one per test.
* **Why rejected:** it is the source of Stryker's longest-standing correctness complaint, where
  process-global state leaks between mutants and inflates scores.

## Consequences

### Positive

* **Uncovered mutants are never run at all**, and are reported `NoCoverage` rather than `Survived`.
  This is the unambiguous win, and it is as much about honesty as speed: "no test reaches this code"
  is a different and often more urgent finding than "a mutant survived".
* A failed instrumented build stops the run with a diagnostic, rather than silently degrading to
  running everything. The instrumented build is also required to pass the suite before anything is
  measured from it: wrapping cannot change what an expression evaluates to, but a coverage map built
  from a program that no longer behaves as it did would look perfectly valid.
* `--no-coverage` is the escape hatch, and also what makes the two paths comparable in a test.

### Negative

* Selection pays only in proportion to how long the suite takes: 29.3 s to 22.6 s for 60 mutants on a
  fixture whose tests do real work, and nothing measurable on a fixture whose tests are instant.
* Process startup is now the floor — roughly 0.5 s to launch a test host against 0.12 s of testing —
  and the obvious lever against it is refused (Alternative 4).
* Not every site can carry a recorder, and that is a third answer rather than a missing one. A site
  whose value is a ref struct, a pointer, `void`, or has no natural type at all cannot be a `Hit<T>`
  argument (RB-017). Nor can a measurement that timed out, crashed or was cut short be read as "this
  test reaches nothing". Both cases resolve to *run every test*, which is slower and never wrong;
  only a site that was measured and found unreached is reported `NoCoverage`.

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

* Reconsider barrier-based attribution if process startup ever stops dominating — a much slower
  suite, or many more tests than mutants. Nothing here forecloses it: it would replace
  `CoverageCollector` and leave the map, the selection and the sandboxes untouched.
