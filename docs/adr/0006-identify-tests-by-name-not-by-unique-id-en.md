# ADR-0006 — Identify tests by name, not by their unique id

**Status:** accepted · **Date:** 2026-08-31

## Context

Milestone 5 will map each mutant to the tests that reach it, so that only those tests run.
Milestone 6 will run mutants in parallel, which requires each concurrent mutant to have its own copy
of the test output directory — otherwise two mutants overwrite each other's assembly.

Those two plans collide, and the collision is in the data model rather than in the code.

An xUnit test carries a unique id, and `--filter-uid` selects by it. But that id is derived from the
**path** of the test assembly. Measured on our own fixture, comparing two byte-identical copies of
the same output directory:

| | stable across copies |
|---|---|
| `ID` (e.g. `a3afdc575d78bd06…`) | **no** — every one differed |
| `DisplayName` (e.g. `Sample.Library.Tests.AgesTests.Adult_age_is_adult`) | **yes** — every one matched |

So a coverage map keyed on unique ids becomes meaningless the moment mutants run in sandboxes: the
ids recorded during the coverage pass would name tests that, in a sandbox, have different ids.
Choosing ids would mean choosing between test selection and parallelism.

## Decision

The coverage map is keyed on **stable test identity** — the fully qualified
`Namespace.Class.Method` — never on the unique id.

Selection uses the runner's name-based filters, verified to work and to compose:

| invocation | tests run |
|---|---|
| no filter | 11 |
| `-method Sample.Library.Tests.AgesTests.Adult_age_is_adult` | 2 (both of its theory cases) |
| the same, twice, for two methods | 3 (the union) |
| `-class Sample.Library.Tests.AgesTests` | 11 |

## Consequences

- Test selection and parallelism become independent. Neither forecloses the other, and M6 can be
  built now without waiting for the coverage design.
- Selection is per *method*, not per theory case: filtering a `[Theory]` by name runs all of its
  cases. Coarser than uid-based selection, and the right granularity anyway — a mutant is killed by
  the first failing case, so splitting a theory would add filtering complexity for no gain.
- The map stays readable. A coverage entry naming
  `Sample.Library.Tests.AgesTests.Adult_age_is_adult` can be understood, diffed and reported;
  `a3afdc575d78bd06…` cannot.
- A renamed test drops out of the map. That is correct: it is a different test, and it must be
  re-measured rather than silently inherit the old test's coverage.
- We give up `--filter-uid`. Nothing is lost that we would have been able to rely on anyway.
