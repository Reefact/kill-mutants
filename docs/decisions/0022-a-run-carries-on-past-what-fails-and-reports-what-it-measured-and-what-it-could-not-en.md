# DEC0022 | A run carries on past what fails, sets it aside with its reason, and reports what it measured and what it could not

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-31 | Proposed | First draft | |
| 2026-10-31 | Accepted | At the maintainer's request | |

## Context

[DEC0005](0005-verify-the-baseline-before-mutating-en.md) verifies the baseline through the mutation
path and aborts the run, reporting no mutation results, when that baseline is not green. It was
written against a single-project fixture whose test run took about 0.6 s.

A run now measures several targets in turn - one project under test with the test projects that
reach it - and each target has its baseline verified before its own mutants are tested. A red
baseline on one target still ends the whole run.

Failures below the target level are already contained. A test host that crashes costs one mutant,
not the run (RB-003), and a mutant that does not compile is set aside rather than stopping anything
(RB-011). Both are outcomes of a mutant inside the measured scope: the report counts a mutant that
cannot be tested as untestable, and under DEC0009 neither changes the exit code.

Discovery reads every project under the directory to find out which are test projects. A project it
cannot read may therefore be a test project, and then nothing tells which production code only it
reaches: those mutants may read as uncovered, or their target may not be selected at all.

On `Reefact/first-class-errors` at `8f84258` (#44), two stops of this kind happened before any result
was reported:

- Discovery stopped 15 seconds in on `tools/floor-check/FloorCheck.csproj`, a project outside the
  solution that no test project references, behind a nested `global.json` pinning an SDK the machine
  did not have (#47).
- The baseline of `FirstClassErrors.RequestBinder.UnitTests` was red 35 minutes into the run because
  of where the sandbox placed the tests (#45), after `Analyzers`, `Cli`, most of `GenDoc` and
  `RequestBinder.Usage` had been judged. The run wrote no report. The verdicts were in the log beside
  the report ([DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md)), and
  `--resume` ([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md))
  recovered 2186 of them, for a user who knew to look.

On that repository, the five targets whose baseline the resumed run verified took between 1.5 s
and 9.6 s each for that baseline alone.

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md) already defines how a
run that did not judge everything reports: the verdicts it reached, without a score, passing no gate.
[DEC0009](0009-exit-codes-are-a-public-contract-en.md) fixes `2` as the exit code of a run that could
not happen, and lists a red baseline among its causes; `1` is a gate that did not pass.

The maintainer's expectation, stated for this decision: a run advances on everything it can at every
level - projects, mutators, mutants - stops only when nothing more can advance, and ends with a report
of what it could do, what it could not and why, with whatever assessment the measured part supports.
The maintainer sees no reason a user would want the run to stop at the first failure instead.

## Decision

In this context, a run sets aside any part of its scope it cannot measure - a project it cannot read,
a target it cannot build or whose baseline is not green - together with the reason, carries on with
everything else, stops early only when nothing more can advance, and ends with a report of what it
measured and what it could not, a score only when the population it covers can be established and
labelled with that part, no gate passed, and exit code `2`, while a mutant that cannot be judged stays
an outcome inside the measured scope.

## Rationale

Each target's baseline is verified before its own mutants, so the guarantee DEC0005 exists for holds
target by target: a red baseline on one target says nothing false about a target whose baseline was
green, and the verdicts of the second remain as trustworthy as in a run where the first was absent.

Continuing is cheap even when the cause is global. If the reconstruction or the environment is broken
for every target, every later baseline fails too, and each failure costs one baseline run - seconds
on the repository measured - before that target is set aside. The hours DEC0005's abort might seem to
save are not spent, because no mutant is tested on a target whose baseline failed.

Stopping is what lost the work. The 35 minutes of verdicts on `first-class-errors` were measured
correctly and were invisible without a report; recovering them depended on knowing the log and
`--resume` existed. A run that carries on reports them by construction.

The same rule already holds below the target, where a crashed host or a mutant that does not compile
costs that mutant and nothing more (RB-003, RB-011). Extending it to projects and targets makes one
rule for every level, which is the maintainer's stated expectation. A mutant that cannot be judged
does not leave any of the scope unmeasured: it is an outcome of the scope, counted as untestable, and
treating it as a failure of the run would let one ordinary mutant turn a complete run red. Only what
leaves part of the intended scope unmeasured is set aside.

A score over the measured part is useful and is only misleading when it reads as the whole; labelled
with the part it covers and printed beside what was set aside, it states what it is. That holds only
when the part it covers is known. A target set aside after discovery leaves the other targets'
populations as they were; a project set aside at discovery may have been the only suite reaching
some code, which then leaves the population itself unknown, and a percentage over an unknown
population is the misreading DEC0010 and DEC0013 refuse. So a score is printed only when nothing was
set aside at discovery. No gate passes, because a gate is a claim about every mutant in its scope -
DEC0013's rule for a run that did not judge everything.

Exit code `2`, because DEC0009 gives it to a run that could not happen and names a red baseline among
its causes; part of this run could not happen, for a reason the user has to fix in the environment or
the suite rather than in the tests. `1` would tell a CI job that the tests got weaker, which is not
what happened. This differs from DEC0013, where the run could have judged everything and was stopped
by a deadline it was given.

A set-aside target leaves its mutants without a verdict, which is exactly what a later `--resume`
inherits around: once the cause is fixed, a resumed run judges only what was set aside and completes
the series with a score over everything (DEC0015).

## Alternatives considered

### Alternative 1 — Keep DEC0005's abort (status quo)

* **Description:** a red baseline, or a project discovery cannot read, ends the run with a
  diagnostic and no results; the log and `--resume` recover what was judged.
* **Why rejected:** it loses correctly measured work behind a mechanism only an informed user knows
  to use, and the target-by-target baseline makes the abort unnecessary for trust in the other
  targets' verdicts.

### Alternative 2 — Stop at the first failure, but report what was judged

* **Description:** end the run at a red baseline as today, writing the report of the targets already
  judged, without a score, as a stopped run does (DEC0013).
* **Why rejected:** it keeps the work already done but forgoes the targets after the failure, which
  are as measurable as those before it; the user then needs a second run for results the first could
  have produced.

### Alternative 3 — Carry on and report no score at all

* **Description:** as decided, but with only the counts of what was judged, never a percentage.
* **Why rejected:** it withholds the assessment the measured part supports, which the maintainer
  asked for; when the covered part is known, a label naming it removes the reason to withhold it.

### Alternative 4 — An option to stop at the first failure

* **Description:** carry on by default, with a flag that restores an immediate stop.
* **Why rejected:** no user need was identified for it. Its only argument is saving computation when
  everything is broken, and a broken target costs one baseline run before it is set aside.

## Consequences

### Positive

* No correctly measured verdict is lost to a failure elsewhere in the run.
* One rule for every level above the mutant: what cannot be measured is set aside with its reason,
  as a mutant that cannot be judged already is within its target.
* The report says what could not be measured and why, where the user reads it, instead of a single
  error that hides everything else.
* A project discovery cannot read, such as `FloorCheck.csproj`, no longer prevents measuring the
  rest of the repository, and the report names it with the reason it could not be read.
* A resumed run completes a series whose earlier run set something aside.

### Negative

* A run with useful results now exits `2`, which a CI job reading only the exit code treats as a tool
  failure; the report has to be read to see what was measured.
* A single project discovery cannot read, anywhere under the directory, withholds the score of the
  whole run until it is excluded or fixed.
* DEC0005's abort and the meaning of a red baseline under DEC0009 change, so the README, the
  architecture document and the messages that describe them must change too.
* The report gains a section for what was set aside, and the score a statement of what it covers.

### Risks

* A score over part of the code may still be quoted as the project's score, whatever its label says.
* A cause shared by every target is reported once per target, which can bury the one message that
  matters.

### Follow-up actions

* Decide whether DEC0005 is superseded by this record, and record it in DEC0005's status.
* Implement the rule at discovery, build, baseline and coverage, with the report section and the exit
  code; revisit #57 (#47), which still stops the run, and reuse #56's diagnostic (#46) as a
  set-aside target's reason.
* Update the README and the architecture document where they describe a red baseline or an
  unreadable project.
