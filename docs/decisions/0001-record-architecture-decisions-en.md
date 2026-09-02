# DEC0001 | Record architecture decisions

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

KillMutants makes a small number of decisions that are expensive to reverse: how a mutant is
applied, how compilation inputs are obtained, how tests are executed.

Six months from now the reasoning behind them will be invisible in the code, and the temptation to
"fix" a deliberate choice will be real.

Not every choice the project makes has that property. Some follow obviously from the project's
stated constraints and had no second reasonable answer.

## Decision

In this context, we record every structuring decision that is hard to reverse and admitted more
than one reasonable answer as a short record in `docs/decisions`, numbered sequentially and never
rewritten.

## Rationale

A decision that is expensive to reverse is exactly the one whose reasoning has to outlive the code
implementing it: the code will be read long before anyone reconstructs why it is shaped that way.

The reasoning is invisible in the code by then, so a record is the only place it can live. Without
one, a deliberate choice is indistinguishable from an accident, which is what makes the temptation
to "fix" it real.

Bounding what earns a record to the two conditions above is what keeps the base to few records, each
worth reading.

## Alternatives considered

*Not recorded at the time of the decision.*

## Consequences

### Positive

There are few records, and each is worth reading.

### Negative

*Not recorded at the time of the decision.*

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

*Not recorded at the time of the decision.*
