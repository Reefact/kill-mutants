# DEC0006 | Identify tests by name, not by their unique id

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

Milestone 5 will map each mutant to the tests that reach it, so that only those tests run. Milestone
6 will run mutants in parallel, which requires each concurrent mutant to have its own copy of the
test output directory — otherwise two mutants overwrite each other's assembly.

Those two plans collide, and the collision is in the data model rather than in the code.

An xUnit test carries a unique id, and `--filter-uid` selects by it. But that id is derived from the
**path** of the test assembly. Measured on our own fixture, comparing two byte-identical copies of
the same output directory:

| | stable across copies |
|---|---|
| `ID` (e.g. `a3afdc575d78bd06…`) | **no** — every one differed |
| `DisplayName` (e.g. `Sample.Library.Tests.AgesTests.Adult_age_is_adult`) | **yes** — every one matched |

The runner also offers name-based filters, verified to work and to compose:

| invocation | tests run |
|---|---|
| no filter | 11 |
| `-method Sample.Library.Tests.AgesTests.Adult_age_is_adult` | 2 (both of its theory cases) |
| the same, twice, for two methods | 3 (the union) |
| `-class Sample.Library.Tests.AgesTests` | 11 |

Those filters select per *method*: filtering a `[Theory]` by name runs all of its cases. A mutant is
killed by the first failing case.

## Decision

In this context, we key the coverage map on stable test identity — the fully qualified
`Namespace.Class.Method` — and never on the unique id.

## Rationale

A coverage map keyed on unique ids becomes meaningless the moment mutants run in sandboxes: the ids
recorded during the coverage pass would name tests that, in a sandbox, have different ids. Since the
ids were measured to differ between two byte-identical copies of one output directory, keying on them
would mean choosing between test selection and parallelism — the two milestones that motivate the map
in the first place.

The name was measured stable across those same copies, so keying on it makes the two plans
independent: neither forecloses the other.

Nothing usable is given up with `--filter-uid`, because the name-based filters were verified to
select and to compose. Their per-method granularity is coarser than uid selection and is the right
granularity anyway: a mutant is killed by the first failing case, so splitting a theory would add
filtering complexity for no gain.

## Alternatives considered

### Alternative 1 — Key the map on the unique id and select with `--filter-uid`

* **Description:** use the identifier the runner already assigns to each test case, and the filter
  built for it.
* **Why rejected:** the id is derived from the test assembly's path, and was measured to differ across
  two byte-identical copies of the same output directory. A map keyed on it survives only as long as
  mutants never run in sandboxes, which forecloses the parallelism of M6.

## Consequences

### Positive

* Test selection and parallelism become independent. Neither forecloses the other, and M6 can be
  built now without waiting for the coverage design.
* The map stays readable. A coverage entry naming
  `Sample.Library.Tests.AgesTests.Adult_age_is_adult` can be understood, diffed and reported;
  `a3afdc575d78bd06…` cannot.

### Negative

* Selection is per *method*, not per theory case: filtering a `[Theory]` by name runs all of its
  cases.
* A renamed test drops out of the map. That is correct — it is a different test, and it must be
  re-measured rather than silently inherit the old test's coverage — but the coverage is lost and has
  to be paid for again.
* We give up `--filter-uid`.

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

*Not recorded at the time of the decision.*
