# ADR-0010 — A partial run reports findings, not a score

**Status:** accepted · **Date:** 2026-09-02

## Context

`--since` will run only the mutants a change touches. That is the feature people ask for first,
because a full sweep of this repository takes minutes and a diff takes seconds, and it is what makes
mutation testing usable on a pull request rather than nightly.

The question this decides is what such a run may print. Every other run of this tool ends with a
mutation score. The obvious thing is to print one here too.

### What a partial score would actually mean

The score is `detected / valid`. What makes two full runs comparable is not that they judge the same
mutants - they do not, since the code itself changes between commits - but that both apply the same
*rule of scope*: every mutable site in the selected codebase. The population moves; the question the
number answers stays put, so a movement in the number is a statement about the suite.

A partial run has no such rule. Its denominator is *the mutants in this diff*, and a diff is not a
scope, it is an accident of what someone happened to touch. Six mutants on Tuesday, ninety on
Wednesday, and nothing in common between them. A run that prints 72 % and then 40 % has not measured
a decline; it has answered two different questions and given both answers the same name, the same
formula, and the same place in the report.

And the number would be badly made as well as badly named. **A small denominator claims a precision
it does not have**: a diff with three mutants and one survivor renders as "66.7 %", three significant
figures on a measurement that carries none.

One trap this does *not* have, which is worth writing down because the obvious version of this
argument gets it wrong. An empty diff does not produce a reassuring headline here:
`MutationScore.IsUndefined` is true when nothing was judged, `ToString` renders `n/a`, and ADR-0009
already makes an undefined score fail a threshold. Reaching for "the empty run reports 100 %" would
have been borrowing another tool's failure without checking our own - which is the error this whole
document is about.

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

It prints the count of each status, so nothing is hidden; the new findings, named, with everything
needed to reproduce each one; and a verdict that is binary, because the question a partial run
answers is binary. A full run asks *how good is this suite?* A partial run asks *did this change
introduce untested behaviour?* Only the first of those has a percentage for an answer.

**The verdict fails on any new *undetected* mutant, not only on a survivor.** A mutant no test
reaches is `NoCoverage`, not `Survived` — and a change that adds code nothing tests at all produces
exactly those. A gate reading only survivors would wave through the clearest possible case of newly
introduced untested behaviour, which is the one thing this run exists to catch. `MutationScore`
already counts both as undetected, and ADR-0007 treats no coverage as often the more urgent of the
two. Both are named in the output, and both fail the verdict.

`CompileError` stays outside it, for the reason it stays outside the score: the suite was never asked
about a mutant the tool could not build.

**No status means "outside the diff".** Mutants a partial run did not consider are not generated, not
counted, and not reported with a state of their own. Adding a status that silently leaves the
denominator is precisely the seam described above, and we would be importing it deliberately.

**The report records the run's scope as metadata.** A partial report whose out-of-diff mutants are
simply absent is indistinguishable from a full run that happened to have that many mutants — so a
dashboard, or a reader six months later, cannot tell what population was inspected or reproduce the
selection. `--report-json` therefore carries the run mode and the *resolved* base and head revisions,
at report level, beside the environment and time budgets already recorded there. That is the same
rule as those: a report that cannot be interpreted is not a report. It is metadata, not a mutant
status, so the previous paragraph stands.

**A threshold is refused with `--since`, from wherever it came.** A threshold means a score, and
there is no score here. The refusal names the option — and when the value came from
`killmutants.json`, it names the file and the key, as every other refusal reading that file does.
That last part is not a detail: `RunSettings` resolves `options.Threshold ?? file?.BreakAt`, so a
project that follows the README and stores `breakAt` would otherwise have every partial run refused
with no way out but editing a versioned file. The way out is `--break-at none`, which clears it for
this run — the same shape as `--without none`, and for the same reason.

**A comparable score from a fast run is a different feature, for later.** Keeping the whole
codebase's denominator and reusing the verdicts of unchanged mutants — incremental *computation*
rather than an incremental *population* — produces a number that is genuinely comparable with a full
run. That feature earns a percentage. `--since` does not, and the two must not be conflated.

## Consequences

- `--since` cannot be used as a percentage gate. That is the point, not a limitation to work around
  later: the gate it offers instead — no new undetected mutant — is a stronger statement about a
  change than any threshold over six mutants would be.
- **This broadens exit code `1`, and says so.** [ADR-0009](0009-exit-codes-are-a-public-contract-en.md)
  defined `1` as *the mutation score is below `--break-at`*, and a partial run has no score. Rather
  than let the table and the behaviour disagree, `1` now means what that ADR's own reasoning always
  said it meant — *the thing you asked me to check is not good enough* — with the score below a
  threshold and the new undetected mutant as its two cases. ADR-0009 is amended in the same change;
  automation reading `1` still learns "findings", which is what it acts on. `2` is unchanged.
- Two reports can no longer be compared by reading one number each, because we no longer print a
  number that invites it. The status counts are comparable and mean what they say.
- A partial report can be told apart from a full one, and its selection reproduced, because the run
  mode and the resolved revisions are in it.
- If the baseline feature is built later, this ADR is where its denominator is already argued.
