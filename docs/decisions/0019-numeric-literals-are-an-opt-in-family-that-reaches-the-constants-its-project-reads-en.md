# DEC0019 | Numeric literals are an opt-in family, moved one step each way, that reaches the constants its project reads

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-29 | Proposed | First draft | |
| 2026-10-29 | Accepted | | |

## Context

Issue #20, filed from the comparison of the catalogue with Stryker.NET on `Reefact/slugger` (#15),
asks for numeric literals as an opt-in family. It is not a gap against Stryker: read at Stryker.NET's
commit `b826642`, none of its mutators rewrites a numeric literal. `docs/architecture-en.md` listed
numeric literals as a future candidate, "classic and high signal, but noisy enough that it wants its
own opt-in rather than a place in the default set".

Stryker.NET makes a mutator optional through a level. Every mutator declares one of `Basic`,
`Standard`, `Advanced` and `Complete`, a run applies the mutators up to its level, and the default is
`Standard`; `Math`, `StringMethod`, `StringMethodToConstant`, `Regex` and `CollectionExpression` are
`Advanced`, so raising the level turns all of them on at once. Stryker.NET never mutates a `const`
field or a `const` local: its orchestrator refuses both, because a mutant there cannot be switched on
at run time.

slugger carries roughly 175 numeric literals in `src`, by a crude count. The ones #20 points to are
domain thresholds held in constants, such as `public const int MinimumNouns = 100;`, which a test
suite can assert around rather than at. #20 describes what kept the family useful in the tools its
author has seen: values in constants and initializers before loop counters and indices, `n + 1` and
`n - 1` rather than `n` to `0`, and `0` and `1` left alone unless they are a constant's value. It
states that `n ± 1` on constants and field initializers would already answer its question.

In KillMutants, every family belongs to the default catalogue. `--mutators` names the only families
to run and `--without` drops some; nothing adds a family to the default catalogue. Each mutant is a
compilation of its own (DEC0002), so the project under test is recompiled with every mutant, while the
test assembly is not. `MutationSite.IsObservable` keeps every family out of a `const`, because C#
copies a constant's value into each assembly that reads it, and a mutant was verified to leave a
reading test assembly on the original value. The coverage probe is a call, and a constant's
initializer must be a constant (CS0133).

The type of an unsuffixed integer literal follows from its size, so `2147483647 + 1` is a `uint`. A
constant converts implicitly to a narrower integer only when it fits: `byte b = 256` fails with
CS0031. An enum accepts the literal `0` and no other.

On 2026-10-29 the maintainer asked for simple but effective decisions, and arbitrated four points:
the family reaches a `const` only where the project under test reads it; it is turned on by naming it
in `--mutators` or adding it with a new `--with`; it reaches the initializers of constants, fields and
properties only, `0` and `1` included; it moves an integer one step each way, and leaves other
numbers alone.

## Decision

An opt-in `NumericLiteral` family, run when `--mutators` names it or `--with` adds it, moves each
integer literal in the initializer of a constant, a field or a property one step up and one step
down, and reaches a `const` only where the project under test reads it.

## Rationale

Opt-in is what the catalogue already said the family needed: a literal is everywhere, and a project
that did not ask for it should not pay a run per literal. Naming the family is enough with one
optional family; Stryker's levels exist to turn a group on, and here a level would name a list of one.
`--with` is the missing half of `--without`: it turns the family on without retyping the default
catalogue, while `--mutators NumericLiteral` still runs it alone.

One step each way asks about the boundary, which is the question #20 raises: a suite that tests
"99 nouns is refused, 100 is accepted" kills both, and one that tests only far from the threshold
kills neither. The two mutants are the two sides of the boundary, as `Comparison`'s shift is for an
operator. Integers only, because one step on a decimal or a floating value is no step at the scale
the value is written at.

Initializers only, because a value given a name is a value somebody decided, while a `0` or a `1` in
a loop or an index usually is not; that is #20's own first version. Inside an initializer, `0` and
`1` are such a decision too, so they are kept.

A `const` was the family's main target and the one place the catalogue refused it. The refusal holds
for a constant only the tests read, whose mutant survives whatever they assert; it does not hold for
one the project reads itself, since the project is recompiled with the mutant. Reaching the second
and not the first keeps every mutant one a test can kill. The other families keep the refusal
unchanged, so the default catalogue generates exactly what it did.

A neighbour that would change the literal's type, or no longer fit the type it is used as, is not
proposed, because such a mutant either does not compile or asks something other than the boundary.

## Alternatives considered

### Alternative 1 — Leave constants out, as every other family does

* **Description:** keep `IsObservable`'s refusal for the new family too.
* **Why rejected:** the family would miss most of the thresholds #20 is about, which are constants.

### Alternative 2 — Mutate every constant

* **Description:** reach a `const` whoever reads it.
* **Why rejected:** a constant only the tests read yields a mutant that survives whatever they
  assert, reported as a gap that does not exist.

### Alternative 3 — `--mutators` only, with no `--with`

* **Description:** turn the family on by naming it among the only families to run.
* **Why rejected:** running it beside the default catalogue would mean naming every family.

### Alternative 4 — A level, as Stryker.NET has

* **Description:** a `--mutation-level` option whose higher value turns the optional families on.
* **Why rejected:** with one optional family, a level is a second name for that family.

### Alternative 5 — Every literal except `0` and `1`, or every literal

* **Description:** reach the literals of method bodies too, `random.Next(100)` and `< 100` among them.
* **Why rejected:** more noise, loop counters and indices among it, and `Comparison` already shifts the
  boundary of `< 100`.

### Alternative 6 — `n + 1` only, or every numeric type

* **Description:** one mutant per literal, or the same step on decimal and floating values.
* **Why rejected:** one mutant tests one side of the boundary only; one step on `0.05m` is not a step.

## Consequences

### Positive

* A threshold asserted around rather than at is reported, which no family could do before.
* The default catalogue is unchanged, so a project that does not ask pays nothing.
* `--with` turns on any future opt-in family the same way.

### Negative

* A mutant inside a constant carries no recorder, since the probe is not a constant, and is tested
  against the whole suite.
* A constant read only through another constant, an attribute or a parameter's default is left
  alone, although the project may read it in the end.
* Generation indexes every identifier of the project, once, as soon as a constant holds an integer
  literal, to find whether the project reads that constant.

### Risks

* A literal whose two neighbours make no observable difference - a capacity, a buffer size - yields
  mutants that cannot be killed.

### Follow-up actions

* Update the catalogue table and the inventory of what is not mutated in `architecture-en.md` and
  `architecture-fr.md`, and the `README.md`.
* Measure the family on slugger.
* Revisit the literals of method bodies once that measurement is in.
