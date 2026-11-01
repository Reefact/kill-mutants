# DEC0027 | `Linq` runs by default, and `--without` leaves it out

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-01 | Proposed | First draft | |
| 2026-11-01 | Accepted | At the maintainer's request | |

## Context

DEC0024 added the `Linq` family as opt-in "first", with its default status "decided after it has
been measured" by a new record. Its follow-up actions named two measurements, on `Reefact/slugger`
and on `Reefact/first-class-errors` at `8f84258`. Neither has been made, and the maintainer notes
that both repositories use very little LINQ, so neither would measure the family where it has
mutants to make. Issue #82 asks the question the record deferred.

One measurement exists, on a third repository: `Reefact/enum-member-name-binding` at `bc6eda9`,
run with KillMutants `13b0229` and `--with Linq,NumericLiteral`, beside Stryker.NET 5.0.0 on the
same commit and machine with two workers each. The default catalogue made 308 mutants there, 26 of
which survived. `Linq` made 5: four `Any` ↔ `All` swaps, all killed, and one `Take(2)` → `Skip(2)`
that survived on a line building the example text of an OpenAPI description. No `Linq` mutant failed
to compile, and none read as near-equivalent. A sixth site, `OrderByDescending` → `OrderBy` on a
sort key, got no mutant because RB-016 refused the call over an `out` variable in its lambda; #78
lifts that refusal, and the mutant, applied by hand, is killed with three failures.

Stryker.NET runs its Linq mutator by default, at its `Standard` level. On the same commit it made 22
Linq mutants: 7 killed, 2 survived, 1 uncovered, 14 that did not compile (`Count` → `Sum` ten times,
`Append` → `Prepend` three, `FirstOrDefault` → `First` once). Its `OrderBy` survivor is killed by
the full suite when applied by hand; its other survivor is the same `Take` → `Skip`. Of its 22, the
five in KillMutants' pairs are the four `Any` ↔ `All` and the `OrderBy`.

Earlier samples of Stryker's family, on `first-class-errors` (#53, #63), gave two plausible
`Any` → `All` gaps and six near-equivalent `FirstOrDefault` → `First`; DEC0024's eleven pairs were
chosen to keep the former and leave out the latter.

A family in the default catalogue changes the score of every project the tool runs on. DEC0020 put
`Statement` in the default catalogue on a measurement; DEC0019 put `NumericLiteral` opt-in because a
literal is everywhere and a project that did not ask should not pay a run per literal. DEC0024's
count by name suggested about thirty `Linq` mutants on a repository of three thousand, "which is
`Statement`'s profile rather than `NumericLiteral`'s".

`--without <list>` already removes a family from whatever catalogue a run starts from, and
`--with <list>` adds one to the default; both have a key in `killmutants.json`. A resumed series is
refused when its settings differ from the plan's, and the catalogue is among those settings
(DEC0015). The end-to-end fixtures hold no call among the eleven pairs.

## Decision

In this context, we decide that `Linq` enters the default catalogue, so that it runs unless
`--without` leaves it out.

## Rationale

The bar DEC0024 set was a measurement by KillMutants, and the one that exists is clean: five mutants
on three hundred, every one compiled, four of five killed, and the one survivor a low-value finding
rather than a near-equivalent. The profile is `Statement`'s - a few mutants, a real question each -
and `Statement` entered the default catalogue on exactly that showing. Nothing in the measurement
points the other way.

Stryker.NET, which runs the family by default, produced fourteen compile errors out of twenty-two
on the same code; the twin rule is what keeps those out of KillMutants' five. The family that was
measured is the one DEC0024 narrowed, and the narrowing did what it was for.

Promoting a family was described by DEC0024 as cheap, and withdrawing one as not. `--without Linq`
is the way out for a project that does not want it, with no new option, and a series does not
silently change under the change, because its plan refuses a different catalogue.

The two measurements DEC0024 asked for were not made, and this record does not pretend they were.
They would not have measured much: the two repositories use very little LINQ. The decision rests on
one repository and on Stryker's samples of two others; a measurement on code rich in LINQ becomes a
follow-up action rather than a precondition, because the one measurement gives no reason to expect
it to disagree, and because the way back is one option.

## Alternatives considered

### Alternative 1 — Status quo: `Linq` stays opt-in

* **Description:** keep the family behind `--with Linq` until the two measurements DEC0024 named
  have been made.
* **Why rejected:** a measurement by KillMutants exists and is clean; the family costs five test runs
  per three hundred on it; and most users never see an opt-in family at all - DEC0020's reason for
  not making `Statement` one.

### Alternative 2 — `Any` ↔ `All` by default, the other pairs opt-in

* **Description:** promote the one pair with confirmed true gaps on two repositories, and keep the
  ten others behind `--with`.
* **Why rejected:** it splits a family in two with no mechanism for it - `--with` and `--without`
  act on families - and the ten other pairs produced one low-value survivor and no near-equivalent
  where measured. The near-equivalents DEC0024 feared were in the swaps it left out, not in these.

### Alternative 3 — Measure first, on repositories rich in LINQ

* **Description:** find two public repositories of modest size that use LINQ widely, run
  `--with Linq` on them, then decide on three measurements. The two DEC0024 named would not do: they
  use very little LINQ.
* **Why rejected:** the one measurement gives no reason to expect a different answer, the way back
  is `--without Linq`, and the runs can be made after the fact as well as before; they are kept as a
  follow-up action.

## Consequences

### Positive

* A quantifier, an order or a slice is questioned on every project, with no option to know about.
* The family's measurements will now come from every run rather than from the few that asked.
* `--with Linq` and `--mutators Linq` keep working; only the default changes.

### Negative

* Every project's score moves, by about five mutants per three hundred where measured.
* A series started before this change does not continue across it: its plan names a different
  catalogue, and the run is refused with the difference named (DEC0015).
* The `Take` → `Skip` survivor on example text is a finding a developer reads and sets aside.

### Risks

* `Skip` ↔ `Take` on a sequence of one element, and the other near-equivalents DEC0024 named as
  possible in a given program, now reach every project rather than the ones that asked.
* A repository rich in LINQ may show a different profile from the one measured.

### Follow-up actions

* Move `Linq` from the opt-in list to the default catalogue; update the help, the README, the
  catalogue table in `architecture-en.md` and `architecture-fr.md`, and the CHANGELOG.
* Measure the family on two public repositories of modest size that use LINQ widely, in place of
  the two DEC0024 named, and record the numbers on #82.
* DEC0024's status gains a row saying this record supersedes its opt-in status, at the
  maintainer's call.
