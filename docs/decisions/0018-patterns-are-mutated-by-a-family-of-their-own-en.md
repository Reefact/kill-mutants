# DEC0018 | The patterns of an `is` are mutated by a family of their own, as the catalogue's operators are

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-29 | Proposed | First draft | |

## Context

Issue #19, filed from the comparison of the catalogue with Stryker.NET 5.0.0 on `Reefact/slugger`
(#15), measured 36 comparison sites that Stryker mutated and KillMutants did not, every one of them a
pattern rather than an operator. `docs/architecture-en.md` listed relational patterns as a future
candidate and `is T` as not supported for now, "as one pattern-aware family rather than two rules".

Read at Stryker.NET's commit `b826642`, three mutators cover patterns. `IsPatternExpressionMutator`
negates the root pattern of every `is`, wherever it stands. `RelationalPatternMutator` rewrites `>`
into `<` and `>=`, and each other relational operator likewise. `BinaryPatternMutator` swaps `or` and
`and`. None of them refuses a pattern that declares a variable, and Stryker's run on slugger counted
181 mutants that did not compile.

In KillMutants, `NegatedDecision` (#18) negates an `is` written as a decision - the condition of a
branch or a loop, or a returned `bool` - as `!(x is P)`, and nothing negates an `is` anywhere else.
`Comparison` rewrites `>` into `>=`, its boundary shift, and `<=`, its negation. No family mutates a
relational pattern or the arms of an `or` or an `and`. No family mutates an expression that declares
a variable (RB-016), and the probe cannot wrap anything inside a pattern (RB-019). C# parses `x is T`
as an operator, not as a pattern.

On slugger at `c9cf4a2`, an approximate count of the `is` expressions in its source found about 157:
about 71 declare a variable, as `is not { } terms` does; about 68 test `null`; about 15 hold a
relational pattern or an `or` or `and`, such as `is < 0 or > 100` and `is "." or ".."`. The parser
that held most of #19's 36 sites has since been rewritten.

`not` binds tighter than `or` and `and`. Swapping `or` for `and` between two constants makes a
pattern that never matches. `x is not null and { Length: > 0 }` matches exactly what
`x is { Length: > 0 }` matches. Mutating one arm of a `switch` can make a later arm unreachable, and
the compiler refuses the whole switch (CS8510).

The maintainer asked, on 2026-10-29, for the best option to be taken on each point and for a question
only where the choice was uncertain.

## Decision

A `Pattern` family negates each `is` test as `x is not P`, shifts and negates each relational pattern
inside an `is` as `Comparison` does its operators, and drops each arm of an `or` or an `and` in turn,
while `NegatedDecision` leaves every `is` to it.

## Rationale

One family owns the `is`, as `Comparison` owns `==`: negating it in the family that understands
patterns reaches the `is` that no decision holds, writes the mutant in the form the code was written
in, and stops `NegatedDecision` from judging the same mutant a second time.

The relational pattern asks the question `Comparison` asks of the same operator, so it gets the same
two mutants and a reader of the report the same answers. When it is the whole pattern its negation is
left out, because `x is <= 1` asks what negating `x is > 1` already asks.

Dropping an arm asks what #19 asked of an `or` - is either arm tested, or only one - where swapping
the operator mostly builds a pattern that never matches. An `and` keeps its `not null`, because the
arm beside it already rejects `null` and the mutant would never differ from the original.

The refusals already in the catalogue carry over unchanged: a pattern that declares a variable is not
mutated, and a pattern carries no recorder, so its mutants are tested against the whole suite. A
switch is left out because the failure it brings is the switch's, not the node's.

## Alternatives considered

### Alternative 1 — Stryker's three mutators as they are

* **Description:** negate the root pattern, rewrite `>` into `<` and `>=`, swap `or` and `and`.
* **Why rejected:** the swap mostly builds a pattern that never matches, and `<` departs from the
  catalogue's own relational pair for no gain in what is asked.

### Alternative 2 — Keep the negation in `NegatedDecision` and add the rest

* **Description:** leave `!(x is P)` where it is, and give the new family only the relational
  patterns and the arms.
* **Why rejected:** an `is` that is no decision would stay unmutated, and the two families would
  meet on any `is` written as one.

### Alternative 3 — The patterns of a `switch` too

* **Description:** mutate relational patterns and arms in switch statements and expressions as well.
* **Why rejected:** a mutant there can make a later arm unreachable, which the compiler refuses for
  the whole switch and no check of a single node can see.

## Consequences

### Positive

* An `is` is mutated wherever it stands, not only where it is a decision.
* Relational patterns and the arms of `or` and `and` are mutated for the first time.
* No pattern that declares a variable is mutated into one that cannot compile.

### Negative

* `NegatedDecision` no longer negates an `is`, so its mutants of the same code change identity.
* The mutants inside a pattern carry no recorder and are tested against the whole suite, which is
  slower.
* The patterns of a `switch` stay unmutated.

### Risks

* Dropping an arm may still make an `is` that always or never matches when the arms overlap, which
  would be a mutant that cannot be killed.

### Follow-up actions

* Update the catalogue table and the inventory of what is not mutated in `architecture-en.md` and
  `architecture-fr.md`.
* Measure the family on slugger.
* Revisit the patterns of a `switch` if a check of the whole switch becomes available.
