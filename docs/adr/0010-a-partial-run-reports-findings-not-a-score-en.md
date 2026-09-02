# ADR-0010 — A partial run reports findings, not a score

**Status:** accepted · **Date:** 2026-09-02

## Context

`--since` will run only the mutants a change touches. That is the feature people ask for first,
because a full sweep of this repository takes minutes and a diff takes seconds, and it is what makes
mutation testing usable on a pull request rather than nightly.

**"Touches" has to include the tests.** A change that only deletes an assertion puts no production
code in the diff at all, so a selection reading production files alone finds nothing to run and
reports an empty, passing run - while the mutants that assertion used to kill now survive. That is
precisely the untested behaviour this feature exists to catch, arriving by the door nobody watches.
So the selection is: every mutable site in the changed production code, **plus every mutant covered
by a test in a changed test file**.

That second half only works while the test is still there to be asked about. Delete or rename a test
- or a fixture or helper the tests lean on - and the coverage relation that named the mutants it used
to kill is gone from HEAD along with it, so nothing selects them and the run is green again for
exactly the reason the rule was added. And the obvious widening is not enough, which is worth
spelling out because it looks sufficient: "every mutant that test project covers" is itself computed
from HEAD coverage, and if `T` was the *only* test covering `M`, then `M` left that set the moment
`T` did. Widening along the axis that already lost the information changes nothing.

**So where a change to a test project cannot be attributed from HEAD coverage, the selection widens
to every mutant in the production projects that test project exercises** - a relation
`MutationTestTarget` holds structurally, from project references, so it survives the deletion of any
number of tests. If even that cannot be established, the run is inconclusive.

Slower, occasionally much slower, and never a false green - the same trade the run already makes when
coverage is unknown, and when a filter is too long for a command line. Reading coverage from the base
revision would be the precise answer instead of the conservative one, and it needs stored results
from a previous run: that is the baseline feature, and it is deliberately not this one.

Stryker.NET selects on the same two grounds - their configuration
documentation, verbatim: *"For changes on test project files all mutants covered by tests in that
file will be seen as changed."* Two tools arriving at the same rule is weak evidence on its own, but
it does say the second half is not a theoretical worry.

The question this decides is what such a run may print. Every other run of this tool ends with a
mutation score. The obvious thing is to print one here too.

### What a partial score would actually mean

The score is `detected / valid`. What makes two full runs comparable is not that they judge the same
mutants - they do not, since the code itself changes between commits - but that both apply the same
*rule of scope*: every mutable site in the selected codebase. The population moves; the question the
number answers stays put, so a movement in the number is a statement about the suite.

A partial run has a rule too - the sites its change touches - and it would be too convenient to
pretend otherwise. The difference is what the rule is anchored to. The full run's scope is the
codebase, which is the same object from one run to the next; the partial run's scope is defined
against a base revision chosen per run, so its population is not merely different each time but
different *by construction*, with no relation between one run's and the next's.

A percentage over it is therefore a perfectly meaningful answer to *how well did the suite do on this
change?* - and no answer at all to any question spanning two runs. Print 72 % on Tuesday and 40 % on
Wednesday under one name, one formula and one place in the report, and the reader will draw a trend
from two numbers that share nothing but their units.

The number would also be too coarse to gate on, which is a different complaint from imprecision and a
harder one to argue with. Two detected and one undetected renders as "66.67 %" - pinned by
`MutationScoreTests` - and that is an exact ratio, not a noisy measurement: calling it false
precision would be wrong. **The problem is granularity.** Over three mutants, one verdict moves the
score by 33.3 points, so every threshold between 34 % and 66 % means the same thing and no threshold
can express "slightly worse". A percentage needs a population big enough for it to move in steps
smaller than the decision it informs, and a diff routinely is not one.

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

**But a run that could test nothing has not passed** - and it exits `1`, like its full-run twin.
Excluding untestable mutants one at a time is
right; letting a change whose mutants were *all* untestable report success is not, and the two are
one line apart. ADR-0009 already settled the full-run version of this - an undefined score fails a
threshold, because a run that demonstrated nothing must not let a misconfigured job stay green - and
the partial run inherits it: a selection that produced mutants, none of which could be tested, is
reported as inconclusive and does not pass. A change with no mutants at all is a different thing and
does pass, having nothing to answer for.

The code is not a new choice. `Program.Verdict` already returns `1` for the full-run version of this
- an undefined score against a threshold, with *"No mutant could be tested, so the N% threshold
cannot be shown to be met"* on standard error - so `1` has carried two causes since before `--since`
was thought of, and this is the third. `2` stays what it has always been: the tool could not run. An
all-untestable partial run **did** run; it simply established nothing, and saying so with the code
that means *the gate did not pass* is what stops a misconfigured job going green.

That makes `ScoreBelowThreshold` the wrong name for the constant, and it was already wrong before
this ADR - the undefined-score path returns it too. It is renamed `GateNotPassed` in this change, so
that the implementation's vocabulary and ADR-0009 say the same thing.

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

- `--since` cannot be used as a percentage gate. The gate it offers instead — no new undetected
  mutant — is *not* stronger than every threshold, and claiming so would be the same kind of
  overstatement this document keeps correcting: on a non-empty population it fails under exactly the
  same condition as a 100 % threshold. What it has over a percentage is that it stays meaningful
  when the denominator is six, where a threshold is arithmetic about nothing.
- **This broadens exit code `1`, and says so.** [ADR-0009](0009-exit-codes-are-a-public-contract-en.md)
  defined `1` as *the mutation score is below `--break-at`*, and a partial run has no score. Rather
  than let the table and the behaviour disagree, `1` now means what that ADR's own reasoning always
  said it meant — *the thing you asked me to check is not good enough* — with the score below a
  threshold and the new undetected mutant as its two cases. ADR-0009 is amended in the same change;
  automation reading `1` still learns "findings", which is what it acts on. `2` is unchanged.
- Two reports can no longer be compared by reading one number each, because we no longer print a
  number that invites it. The status counts remain explicit and locally interpretable, and are not
  offered as a cross-run quality metric either: `Killed 5 / Survived 1` beside `Killed 80 /
  Survived 2` is no more a trend than the percentages would have been.
- A partial report can be told apart from a full one, and its selection reproduced, because the run
  mode and the resolved revisions are in it.
- If the baseline feature is built later, this ADR is where its denominator is already argued.
