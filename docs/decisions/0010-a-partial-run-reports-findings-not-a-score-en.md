# DEC0010 | A partial run reports findings, not a score

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-09-02 | Accepted | | |

## Context

`--since` will run only the mutants a change touches. That is the feature people ask for first,
because a full sweep of this repository takes minutes and a diff takes seconds, and it is what makes
mutation testing usable on a pull request rather than nightly. What such a run selects is
[DEC0011](0011-widen-a-partial-run-selection-when-a-test-file-changes-en.md); what it may print is
decided here. Every other run of this tool ends with a mutation score.

The score is `detected / valid`. What makes two full runs comparable is not that they judge the same
mutants - they do not, since the code itself changes between commits - but that both apply the same
*rule of scope*: every mutable site in the selected codebase. The population moves; the question the
number answers stays put, so a movement in the number is a statement about the suite. A partial run
has a rule too - the sites its change touches - and it would be too convenient to pretend otherwise.
The difference is what the rule is anchored to. The full run's scope is the codebase, which is the
same object from one run to the next; the partial run's scope is defined against a base revision
chosen per run, so its population is not merely different each time but different *by construction*,
with no relation between one run's and the next's.

The number would also be too coarse to gate on. Two detected and one undetected renders as
"66.67 %" - pinned by `MutationScoreTests` - and that is an exact ratio, not a noisy measurement:
calling it false precision would be wrong. **The problem is granularity.** Over three mutants, one
verdict moves the score by 33.3 points, so every threshold between 34 % and 66 % means the same thing
and no threshold can express "slightly worse".

One trap this does *not* have, which is worth writing down because the obvious version of this
argument gets it wrong. An empty diff does not produce a reassuring headline here:
`MutationScore.IsUndefined` is true when nothing was judged, `ToString` renders `n/a`, and
[DEC0009](0009-exit-codes-are-a-public-contract-en.md) already makes an undefined score fail a
threshold.

Stryker.NET's behaviour here is read from their documentation, not from their code. Their score is
`detected / valid * 100`, where `valid` is killed + timeout + survived + no coverage. `ignored`,
`compile error` and `runtime error` are outside the denominator, and of `ignored` the documentation
says: "This will not count against your mutation score but will show up in reports." The state itself
is defined as "The mutant wasn't tested because it is ignored. Either by user action, **or for another
reason**." `since` walks through that last clause; their configuration documentation says "Stryker
will only report on mutants within the changed code. All other mutants will not have a result." So a
partial run is implemented on top of a primitive whose meaning is already *this does not count*, and
the denominator quietly becomes the diff while the label, the formula and the position in the report
all stay put. They then shipped a **second** feature, `with-baseline`, to undo this — "provid[ing] you
with a full report after a partial mutation testrun" — and the two are mutually exclusive.

One honesty note: that out-of-diff mutants land specifically in the `ignored` state comes from a
maintainer discussion, not from the reference documentation, which says only "will not have a
result". Both readings amputate the denominator, so the reasoning holds either way, but the weaker
claim is the one we are entitled to.

A mutant no test reaches is `NoCoverage`, not `Survived`, and `MutationScore` counts both as
undetected; [DEC0007](0007-measure-coverage-with-a-type-preserving-probe-en.md) treats no coverage as
often the more urgent of the two. `Program.Verdict` already returns `1` for the full-run version of an
inconclusive result - an undefined score against a threshold, with *"No mutant could be tested, so the
N% threshold cannot be shown to be met"* on standard error. `RunSettings` resolves
`options.Threshold ?? file?.BreakAt`, so a threshold can arrive from `killmutants.json` as well as
from the command line.

## Decision

In this context, we print no mutation score for a partial run — only the count of each status, the new
undetected mutants named with everything needed to reproduce each one, and a binary verdict that fails
on any of them and on a selection none of whose mutants could be tested — we generate no status
meaning "outside the diff", we record the run mode and the resolved base and head revisions in the
report, and we refuse a threshold whatever its source.

## Rationale

A partial run's population is different *by construction* from one run to the next, with no relation
between them. Print 72 % on Tuesday and 40 % on Wednesday under one name, one formula and one place in
the report, and the reader will draw a trend from two numbers that share nothing but their units. A
percentage over that population is a perfectly meaningful answer to *how well did the suite do on this
change?* — and no answer at all to any question spanning two runs.

Granularity rules it out as a gate independently of that. A percentage needs a population big enough
for it to move in steps smaller than the decision it informs, and a diff routinely is not one.

The question a partial run answers is binary, which is why the verdict is. A full run asks *how good
is this suite?*; a partial run asks *did the selected scope produce an undetected mutant?* — the scope
being DEC0011's, and not, despite the temptation to say so, every way a change could introduce
untested behaviour. The narrower question is the one the run can actually answer, and a document about
not overclaiming has to ask it in the words it can defend. Neither question has a percentage for an
answer, but only the first would even be entitled to one.

The verdict fails on any new *undetected* mutant rather than only on a survivor, because a change that
adds code nothing tests at all produces `NoCoverage` rather than survivors; a gate reading only
survivors would wave through the clearest possible case of newly introduced untested behaviour, which
is the one thing this run exists to catch. `CompileError` stays outside it, for the reason it stays
outside the score: the suite was never asked about a mutant the tool could not build.

A run that could test nothing has not passed. Excluding untestable mutants one at a time is right;
letting a change whose mutants were *all* untestable report success is not, and the two are one line
apart. DEC0009 already settled the full-run version — an undefined score fails a threshold, because a
run that demonstrated nothing must not let a misconfigured job stay green — and the partial run
inherits it. A change with no mutants at all is a different thing and does pass, having nothing to
answer for.

The exit code is not a new choice: `1` has carried two causes since before `--since` was thought of,
and this is the third. `2` stays what it has always been — the tool could not run. An all-untestable
partial run **did** run; it simply established nothing, and saying so with the code that means *the
gate did not pass* is what stops a misconfigured job going green.

A status meaning "outside the diff" would reintroduce the seam by another route: it is a state that
silently leaves the denominator, which is precisely what makes Stryker's number change meaning without
announcing it. Importing it knowingly would be worse than inheriting it.

The report records the run's scope because a partial report whose out-of-diff mutants are simply
absent is indistinguishable from a full run that happened to have that many mutants — so a dashboard,
or a reader six months later, cannot tell what population was inspected or reproduce the selection. It
is metadata, not a mutant status, so the paragraph above stands.

A threshold presupposes a score, and there is none here, so it is refused from wherever it came. The
refusal names the option, and when the value came from `killmutants.json` it names the file and the
key, as every other refusal reading that file does. That is not a detail: a project that follows the
README and stores `breakAt` would otherwise have every partial run refused with no way out but editing
a versioned file. The way out is `--break-at none`, the same shape as `--without none`.

Stryker's own design is the strongest external evidence: when a tool needs a second feature to make
the first one readable, the problem is in the design and not in the documentation.

## Alternatives considered

### Alternative 1 — Print a mutation score for the partial run

* **Description:** end a `--since` run with the same `detected / valid` percentage every other run
  prints, computed over the mutants in the diff. This is what Stryker.NET does.
* **Why rejected:** the population is different by construction each run, so two such numbers answer
  no common question even though the label, the formula and their place in the report say they do; and
  over a diff-sized population the number is too coarse to gate on.

### Alternative 2 — Add a status meaning "outside the diff"

* **Description:** generate every mutant and mark those the partial run did not consider with a state
  of their own, as Stryker's `ignored` does.
* **Why rejected:** such a state silently leaves the denominator, which is the seam that makes a
  partial score change meaning without announcing it. Adopting it would be importing that seam
  deliberately.

### Alternative 3 — Accept a threshold with `--since`

* **Description:** keep the threshold option working in partial runs, either ignored or reinterpreted
  against the diff.
* **Why rejected:** a threshold means a score, and there is no score here. Both variants are the silent
  reinterpretation this decision exists to refuse.

### Alternative 4 — Fail the verdict on survivors only

* **Description:** treat a new `Survived` mutant as the failure condition and leave `NoCoverage` out of
  the gate.
* **Why rejected:** a change that adds code nothing tests at all produces `NoCoverage`, not survivors.
  Such a gate would wave through the clearest case of newly introduced untested behaviour — the one
  thing the run exists to catch.

### Alternative 5 — Keep the whole codebase's denominator by reusing unchanged verdicts

* **Description:** incremental *computation* rather than an incremental *population* — reuse the
  verdicts of mutants a change does not touch, so the denominator stays the codebase and the number
  stays comparable with a full run.
* **Why rejected:** it is a different feature, not a way of scoring `--since`. It earns a percentage
  precisely because it keeps the full population, which `--since` does not have, and conflating the two
  is what this decision refuses.

## Consequences

### Positive

* A partial report can be told apart from a full one, and its selection reproduced, because the run
  mode and the resolved revisions are in it.
* The status counts remain explicit and locally interpretable.
* If the baseline feature is built later, this record is where its denominator is already argued.

### Negative

* `--since` cannot be used as a percentage gate. The gate it offers instead — no new undetected mutant
  — is *not* stronger than every threshold, and claiming so would be the same kind of overstatement
  this record keeps correcting: on a non-empty population it fails under exactly the same condition as
  a 100 % threshold. What it has over a percentage is that it stays meaningful when the denominator is
  six, where a threshold is arithmetic about nothing.
* Two reports can no longer be compared by reading one number each. The status counts are not offered
  as a cross-run quality metric either: `Killed 5 / Survived 1` beside `Killed 80 / Survived 2` is no
  more a trend than the percentages would have been.
* This broadens exit code `1`. [DEC0009](0009-exit-codes-are-a-public-contract-en.md) defined it as
  *the mutation score is below `--break-at`*, and a partial run has no score; rather than let the
  contract and the behaviour disagree, `1` now means what that record's own reasoning always said it
  meant. Automation reading `1` still learns "findings", which is what it acts on. `2` is unchanged.

### Risks

* A passing partial run says the selected scope produced no undetected mutant, which is a narrower
  claim than "this change introduced no untested behaviour". A reader who takes the broader reading
  gets more assurance than the run gives, and DEC0011 names a shape the scope cannot see.

### Follow-up actions

* DEC0009 is amended in the same change, so the contract and the behaviour do not disagree, and the
  constant is renamed `GateNotPassed` — `ScoreBelowThreshold` was already wrong for the undefined-score
  path before this record existed.
