# DEC0017 | A ternary's condition is forced each way, still evaluated, rather than its branches swapped

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-29 | Proposed | First draft | |
| 2026-10-29 | Accepted | | |

## Context

The `Conditional` family rewrites a ternary `c ? a : b` into `c ? b : a`: one mutant per ternary. It
skips a ternary whose two branches are the same expression, and no family mutates an expression that
declares a variable (RB-016) or assigns one in a part C# may skip (RB-030).

Issue #16, filed from the comparison of the catalogue with Stryker.NET 5.0.0 on `Reefact/slugger`
(#15), asked to force a condition to a constant, "the way Stryker's Conditional family does", on the
conditions of `if`, `while`, `do`, `for` and the ternary. Read at Stryker.NET's commit `b826642`,
`ConditionalExpressionMutator` applies to the ternary only. It produces `(true ? a : b)` and
`(false ? a : b)`, so the condition is no longer evaluated, and it refuses only a condition that
declares a pattern variable. Its only mutation of an `if` or a `while` condition is a negation
(`NegateConditionMutator`), which KillMutants' `NegatedDecision` already makes (#18).

The three mutants relate exactly. On a test where `c` is false, the swap and the condition forced to
true both yield `a` where the original yields `b`; on a test where `c` is true, the swap and the
condition forced to false both yield `b` where the original yields `a`. A test that observes the
ternary therefore kills the swap if and only if it kills one of the two forced mutants. A forced
mutant also says which branch no test checks, which the swap cannot.

The catalogue does not silently drop a side effect: `NullCoalescing` keeps only its left operand, and
swaps its operands only when the left one merely reads a variable, a field or an auto-implemented
property (RB-015). KillMutants has no analysis that decides whether an arbitrary condition is free of
side effects. `c || true` and `c && false` evaluate `c` and are `bool` wherever `c` is; neither is a
compile-time constant, so neither changes what the compiler concludes about reachability (RB-029).

A loop forced to `true` does not end, and each such mutant would spend the whole timeout budget
(RB-010). On `Reefact/slugger`, `NegatedDecision` found 103 `if` and `while` conditions; the last full
run (run 38422204935) judged 63 `Conditional` mutants, of which 57 were killed, 5 survived and 1 was
not covered. Every mutant costs a test run.

The maintainer chose, on 2026-10-29, the ternary only, a form that keeps the condition evaluated, and
the forced mutants in place of the swap.

## Decision

The `Conditional` family turns each ternary `c ? a : b` into `(c || true) ? a : b` and
`(c && false) ? a : b`, in place of the branch swap `c ? b : a`.

## Rationale

The two forced mutants carry everything the swap carries, since the swap dies exactly when one of
them does, and they add which branch goes unchecked. Keeping the swap beside them would pay a test
run per ternary for no verdict it could add.

Writing the forced value beside `c` rather than instead of it keeps `c`'s side effects, which is the
catalogue's rule, without the purity analysis KillMutants does not have. The form binds wherever `c`
does and settles nothing at compile time, so it brings back neither a mutant that cannot compile nor
RB-029's instrumentation failure.

The ternary is where Stryker forces a condition, and where the swap stood. An `if` or a loop is
already asked about by `NegatedDecision`, forcing a loop to `true` buys a timeout per mutant, and on
slugger it would add some two hundred mutants on top of the ternaries' share.

## Alternatives considered

### Alternative 1 — Keep the swap alone (status quo)

* **Description:** leave `Conditional` as `c ? b : a` and close #16.
* **Why rejected:** the swap cannot say which branch is unchecked, and a forced mutant can.

### Alternative 2 — Stryker's form, `true ? a : b` and `false ? a : b`

* **Description:** replace the condition by the literal, as Stryker.NET does.
* **Why rejected:** it drops `c`'s evaluation and any side effect in it, which the catalogue refuses;
  restricting it to conditions free of side effects would need an analysis KillMutants does not have.

### Alternative 3 — The forced mutants beside the swap

* **Description:** three mutants per ternary.
* **Why rejected:** the swap's verdict follows from the other two, so it would cost a test run per
  ternary and add nothing.

### Alternative 4 — Force the conditions of `if` and loops too

* **Description:** two forced mutants on every branch and loop condition, as #16 first asked.
* **Why rejected:** Stryker does not do it, `NegatedDecision` already asks about those decisions, a
  loop forced to `true` costs a timeout per mutant, and the count on slugger would grow by some two
  hundred.

## Consequences

### Positive

* A surviving `Conditional` mutant names the branch no test checks.
* No side effect of the condition is dropped, unlike Stryker's form.
* On the ternary, the catalogue covers what Stryker's `Conditional` covers.

### Negative

* Two mutants per ternary instead of one: on slugger, some 63 more test runs.
* The family's mutants change identity, so `Conditional` verdicts reached before the change cannot be
  compared with those reached after it, and a series resumed across it judges them again.
* The report shows `(c || true) ? a : b`, which reads less directly than a label saying the condition
  was forced to true.

### Risks

* A ternary whose condition is not a `bool` but a type with its own `operator true` may not bind
  `c || true`; the family has to ask the compiler, as RB-011 requires.

### Follow-up actions

* Rewrite issue #16's description to the ternary scope this record settles.
* Update the catalogue table in `architecture-en.md` and `architecture-fr.md`.
* Measure the family on slugger before and after.
