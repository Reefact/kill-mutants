# DEC0011 | Widen a partial run's selection when a test file changes

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-09-02 | Accepted | | |

## Context

`--since` will run only the mutants a change touches. That is the feature people ask for first,
because a full sweep of this repository takes minutes and a diff takes seconds, and it is what makes
mutation testing usable on a pull request rather than nightly. What such a run may print is
[DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md); what it selects is decided here.

**"Touches" has to include the tests.** A change that only deletes an assertion puts no production
code in the diff at all, so a selection reading production files alone finds nothing to run and
reports an empty, passing run - while the mutants that assertion used to kill now survive. That is
precisely the untested behaviour this feature exists to catch, arriving by the door nobody watches.

Reading changed test files is not enough on its own, because it only works while the test is still
there to be asked about. Delete or rename a test - or a fixture or helper the tests lean on - and the
coverage relation that named the mutants it used to kill is gone from HEAD along with it, so nothing
selects them and the run is green again for exactly the reason the rule was added. And the obvious
widening is not enough either, which is worth spelling out because it looks sufficient: "every mutant
that test project covers" is itself computed from HEAD coverage, and if `T` was the *only* test
covering `M`, then `M` left that set the moment `T` did. Widening along the axis that already lost the
information changes nothing.

The trigger is not "the change cannot be attributed" either, which is a narrower thing than the
problem. What goes missing is a coverage *edge*, not a test *identity*: leave `T` in place and change
a fixture, an input file or a helper it leans on so that it no longer reaches `M`, and `T` is still
perfectly attributable while `T -> M` is gone. HEAD cannot tell us that edge ever existed - proving a
disappearance needs the run before, which is exactly what we do not have.

The same hole reappears one layer down if the project graph is read at HEAD alone. Remove the
`ProjectReference` from `Tests` to `ProjectA` in the very change being judged, and the HEAD graph no
longer says `Tests` exercises `ProjectA`: the fallback asks a question whose answer the change has
already deleted. First the vanishing relation was `T -> M`; here it is `Tests -> ProjectA`.

Coverage history needs a previous *run*; structural history needs only the two *revisions*, and git
has both.

A test *added* by the change cannot remove an edge that predates it. Modifying an existing file is not
the same case, because nothing cheap distinguishes a modification that adds a test from one that
removes an assertion.

Test support often lives in a plain class library beside the tests - builders, fakes, clocks,
generated inputs - and `ProjectDiscovery` classifies projects only by `IsTestProject`, so such a
library is a *mutable target* exactly like the code under test.

Stryker.NET selects on the same two grounds - their configuration documentation, verbatim: *"For
changes on test project files all mutants covered by tests in that file will be seen as changed."*
That two tools reach the same starting rule is weak evidence on its own, but it does say the second
half is not a theoretical worry. It also stops there: "all mutants covered by tests in that file" is
read from the current run, so it inherits the same edge-loss hole.

## Decision

In this context, we select a partial run's mutants from every mutable site in the changed production
code and every mutant covered by a test in a changed test file, and — whenever a change touches an
existing test, fixture, helper or configuration file in a test project — we widen the selection to
every mutant in the production projects that test project exercises, read from the project graph at
both the base and the head revision.

## Rationale

A selection reading production files alone is defeated by the change it most needs to catch: deleting
an assertion. Reading changed test files closes that, and nothing else does.

Reading them is not sufficient on its own because the information the rule depends on can be the very
thing the change removed. Widening along the coverage axis does not help, since that set is computed
from the same HEAD coverage that already lost the edge; the widening therefore has to run along a
relation the change cannot silently erase. `MutationTestTarget` is that relation: it comes from
project references rather than from observed coverage.

The graph is read at both revisions rather than at HEAD alone because otherwise the fallback inherits
the failure it exists to prevent, one layer up. And that is affordable here where the precise answer
is not: coverage history would need a previous run, which is the baseline feature; structural history
needs only the two revisions, and git has both. There is no equivalent excuse, so the base-side graph
is resolved rather than assumed, and a graph that cannot be resolved makes the run inconclusive rather
than trusted to HEAD alone.

A test added by the change is a principled exception rather than a concession: a new test cannot
remove an edge that predates it, so HEAD attribution is sound there and the precise rule still
applies.

The cost is a slower run, occasionally much slower — the same trade the run already makes when
coverage is unknown, and when a filter is too long for a command line. Conservative and slow beats
precise and wrong when the failure mode is a green run that should have been red.

## Alternatives considered

### Alternative 1 — Select from changed production files alone

* **Description:** read the diff for mutable sites and stop there, leaving test files out of the
  selection.
* **Why rejected:** a change that only deletes an assertion puts no production code in the diff, so
  the run is empty and green while the mutants that assertion used to kill now survive — the exact
  case the feature exists to catch.

### Alternative 2 — Widen to every mutant the changed test project covers

* **Description:** when a test file changes, select everything that test project is known to cover,
  rather than following the project graph.
* **Why rejected:** that set is computed from HEAD coverage, and if `T` was the only test covering
  `M`, `M` left it the moment `T` did. Widening along the axis that already lost the information
  changes nothing.

### Alternative 3 — Trigger on a change that cannot be attributed to a test

* **Description:** widen only when the tool cannot tell which test a change belongs to.
* **Why rejected:** it is narrower than the problem. What goes missing is a coverage edge, not a test
  identity: `T` can stay perfectly attributable while `T -> M` disappears.

### Alternative 4 — Read the project graph at HEAD only

* **Description:** compute the widening from the head revision, as the coverage map is.
* **Why rejected:** removing a `ProjectReference` in the change being judged deletes the answer the
  fallback is about to ask for. The hole the widening exists to close reappears one layer up.

### Alternative 5 — Widen on any changed project reachable from a test project

* **Description:** re-run everything a test project can reach transitively whenever any of it changes,
  which would also cover the support-library gap.
* **Why rejected:** every production change would re-run everything, which is `--since` abolished
  rather than qualified, and no structural fact separates a support library from a subject.

### Alternative 6 — Read coverage from the base revision

* **Description:** the precise answer rather than the conservative one — ask the previous run which
  tests reached which mutants.
* **Why rejected:** it needs stored results from a previous run. That is the baseline feature, and it
  is deliberately not this one.

## Consequences

### Positive

* The change this feature most needs to catch — an assertion deleted with no production code in the
  diff — is selected rather than silently passed.
* The widening runs along a relation the change cannot erase without the tool noticing, and it is
  resolved at both revisions rather than assumed from one.
* A test *added* by a change still gets the precise selection, because the imprecise case cannot arise
  there.

### Negative

* Runs are slower, occasionally much slower: touching one test file can re-run every mutant in the
  production projects that test project exercises.
* The widening is conservative rather than precise, and stays that way until a previous run's coverage
  is available to consult.

### Risks

* The guarantee stops at the edge of a test project. Test support in a plain class library is a mutable
  target rather than a test project, so changing it can stop `T` reaching `M` while neither the test
  project nor `M`'s project appears in the diff, and the run passes without having asked about `M`.

### Follow-up actions

* Close RB-025 with baseline coverage, or with an explicit way to declare a project as test support.
