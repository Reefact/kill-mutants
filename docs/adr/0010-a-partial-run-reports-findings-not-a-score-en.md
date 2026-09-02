# ADR-0010 — A partial run reports findings, not a score

**Status:** accepted · **Date:** 2026-09-02

## Context

`--since` will run only the mutants a change touches. That is the feature people ask for first,
because a full sweep of this repository takes minutes and a diff takes seconds, and it is what makes
mutation testing usable on a pull request rather than nightly.

The question this decides is what such a run may print. Every other run of this tool ends with a
mutation score. The obvious thing is to print one here too.

### What a partial score would actually mean

The score is `detected / valid`. In a full run the denominator is *this codebase*, and that is what
makes two runs comparable: the population is the same, so a movement in the number is a movement in
the suite.

In a partial run the denominator is *the mutants in this diff*. That population is different every
time. Six mutants on Tuesday, ninety on Wednesday. A run that prints 72 % and then 40 % has not
measured a decline; it has measured two unrelated things and given them the same name, the same
formula, and the same place in the report.

Two smaller consequences make it worse rather than merely imprecise:

- **Small denominators claim precision they do not have.** A diff with three mutants and one
  survivor is "66.7 %" — three significant figures on a measurement that carries none.
- **The empty diff is the loudest case.** A change with no mutants, or whose mutants are all
  outside the tested code, produces the most reassuring number available. That is the shape of
  failure this project exists to refuse: the headline is at its best exactly where the evidence is
  at its weakest.

### What Stryker.NET does, and what it cost them

Read from their documentation, not from their code.

Their score is `detected / valid * 100`, where `valid` is killed + timeout + survived + no coverage.
`ignored`, `compile error` and `runtime error` are outside the denominator, and of `ignored` the
documentation says: "This will not count against your mutation score but will show up in reports."
The state itself is defined as "The mutant wasn't tested because it is ignored. Either by user
action, **or for another reason**."

`since` walks through that last clause. Their configuration documentation: "Stryker will only report
on mutants within the changed code. All other mutants will not have a result."

So a partial run is implemented on top of a primitive whose meaning is already *this does not count*.
It is an economical implementation, and it is exactly why the number changes meaning without
announcing it: the denominator quietly becomes the diff while the label, the formula and the position
in the report all stay put.

The strongest evidence is in their own design. They shipped a **second** feature, `with-baseline`, to
undo this — "provid[ing] you with a full report after a partial mutation testrun" — and the two are
mutually exclusive. When a tool needs a second feature to make the first one readable, the problem is
in the design and not in the documentation.

One honesty note: that out-of-diff mutants land specifically in the `ignored` state comes from a
maintainer discussion, not from the reference documentation, which says only "will not have a
result". Both readings amputate the denominator, so the reasoning holds either way, but the weaker
claim is the one we are entitled to.

## Decision

**A partial run prints no mutation score.**

It prints the count of each status, so nothing is hidden; the new survivors, named, with everything
needed to reproduce each one; and a verdict that is binary, because the question a partial run
answers is binary. A full run asks *how good is this suite?* A partial run asks *did this change
introduce untested behaviour?* Only the first of those has a percentage for an answer.

**No status means "outside the diff".** Mutants a partial run did not consider are not generated, not
counted, and not reported with a state of their own. Adding a status that silently leaves the
denominator is precisely the seam described above, and we would be importing it deliberately.

**`--break-at` is refused with `--since`**, by name, saying why — not accepted and quietly ignored,
and not silently reinterpreted against the diff. A threshold means a score, and there is no score
here. The refusal names the option and the flag, in the manner of every other refusal this tool
makes.

**A comparable score from a fast run is a different feature, for later.** Keeping the whole
codebase's denominator and reusing the verdicts of unchanged mutants — incremental *computation*
rather than an incremental *population* — produces a number that is genuinely comparable with a full
run. That feature earns a percentage. `--since` does not, and the two must not be conflated.

## Consequences

- `--since` cannot be used as a percentage gate. That is the point, not a limitation to work around
  later: the gate it offers instead — no new survivors — is a stronger statement about a change than
  any threshold over six mutants would be.
- The exit-code contract of [ADR-0009](0009-exit-codes-are-a-public-contract-en.md) still applies:
  `1` when the run found something the user asked to fail on, `2` when it could not run.
- Two reports can no longer be compared by reading one number each, because we no longer print a
  number that invites it. The status counts are comparable and mean what they say.
- If the baseline feature is built later, this ADR is where its denominator is already argued.
