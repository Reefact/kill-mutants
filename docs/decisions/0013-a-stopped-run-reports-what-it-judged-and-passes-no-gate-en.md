# DEC0013 | A stopped run reports what it judged and passes no gate

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-11 | Proposed | First draft | |
| 2026-10-11 | Accepted | | |

## Context

A run keeps its verdicts in memory and writes its reports once, when it returns. Until this decision
nothing could end a run early and still get a report out of it: the session accepted a cancellation
token, which abandoned the run with an exception, and no handler existed for SIGINT or SIGTERM, so a
signal ended the process with nothing written.

[Issue #24](https://github.com/Reefact/kill-mutants/issues/24) measured the cost on
`Reefact/slugger`, whose workflow gives its job `timeout-minutes: 90`. The nightly full run on `main`
was cancelled at 90 minutes on 6, 8 and 9 October 2026 and took 86 minutes on the 7th. A pull-request
run spent fifty-seven minutes testing mutants on one project and was cancelled with nothing left of
them. Run locally to the end on four cores with `-p 4`, the same scope took 16.1 minutes for one
project (262 mutants) and 88.1 minutes for the other (816 mutants); CI runs with the default, half
the processors.

GitHub Actions documents how it cancels a step: the runner sends SIGINT to the step's entry process -
the shell, for a `run:` step - then a SIGTERM 7.5 seconds later, then kills the process tree 2.5
seconds after that. A process the shell started generally receives no signal before it is killed.
GNU `timeout`, the usual way to send a signal to the tool itself, exits 124 when its time runs out
unless it is given `--preserve-status`, whatever the tool it ran returned.

Mutants are tested project by project in the order discovery found the projects, and within a project
the workers take them in the order they were generated. A mutant that times out is re-run alone,
once the workers have finished, before the timeout is believed; a timeout counts as a detection. A
test host that ends abnormally is reported as a crash, and a crash counts as a kill.

The exit codes are a public contract ([DEC0009](0009-exit-codes-are-a-public-contract-en.md)): `0`
for a run whose gate passed or that was given none, `1` for a gate that did not pass, `2` for a run
that could not happen, `64` for a command line that was not understood; that record's own reading is
that a job fails the build on `1` and pages someone on `2`. A new cause of an outcome a code already
names joins that code. An undefined score does not meet a threshold. A partial run is always a gate
([DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md)), prints no score, and exits `1`
when every mutant it selected was untestable, on the grounds that such a run did run and simply
established nothing. A partial run whose comparison is incomplete withholds its pass and exits `1`
([DEC0012](0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-en.md)).

## Decision

A run stopped before every mutant it selected has a verdict - by a deadline given with
`--stop-after`, or by the first SIGINT or SIGTERM it receives - reports the verdicts it reached
without a score or a verdict and passes no gate, exiting `1` when a gate was asked for and `0` when
none was.

## Rationale

A deadline the tool sets itself is the mechanism that works where the runs were lost. On GitHub
Actions the signal goes to the shell and the tree is killed seconds later, so a tool that waits to
be told never is; making it hear would take an `exec` or a `timeout` in every workflow, and `timeout`
would then replace the exit code with 124. A deadline depends on nothing being delivered. The
signals are still handled, for the person at a terminal and for whatever does deliver one, and only
the first is taken over, so a second still ends the process at once.

No score, because the mutants judged are not a sample. They are whole projects in discovery order
and then the front of the next one, so a percentage over them describes the files that happened to
come first. That is DEC0010's objection - a percentage over a population that is not the codebase -
arrived at by another route.

No gate passes, because a gate is a claim about every mutant in its scope and a stopped run has
judged some of them. That is the rule an undefined score already follows against a threshold and an
incomplete comparison already follows on a partial run: a run that has not established the answer
does not report the one that passes. Stopping is a new cause of an outcome `1` already names, so it
joins `1` rather than taking a code of its own.

`1` rather than `2`, because the run ran. DEC0010 drew the same line for a partial run whose mutants
were all untestable. `2` means the tool could not work, and a nightly run that keeps meeting the
deadline it was given has broken nothing - read as a tool failure every night, it would be paged or
switched off. With no gate asked for, `0`, as for every run that only reports; standard error says
that it stopped.

A verdict reached while the run is coming down is not kept. Stopping ends the test hosts the run is
waiting on, and a signal that stops the tool may have reached a host as well; either way the host
reads as a crash, and a crash counts as a kill. Keeping it would credit the suite with a detection
the stop caused, which is the direction of error the timeout confirmation already exists to prevent.
For the same reason a timeout that has not been re-run alone has no verdict yet.

## Alternatives considered

### Alternative 1 — Keep the status quo

* **Description:** a run is only ever reported when it ends; a signal or a CI timeout ends the process
  and nothing is written.
* **Why rejected:** an hour of measurement becomes no information at all, which is what happened on
  three nights out of four on `Reefact/slugger`.

### Alternative 2 — Handle the signals only

* **Description:** write the report on SIGINT and SIGTERM, and leave it to the workflow to make sure
  the tool receives one, as the issue first proposed.
* **Why rejected:** on GitHub Actions the tool receives nothing unless every workflow `exec`s it or
  wraps it in `timeout`, and `timeout` replaces the exit code with 124.

### Alternative 3 — Handle the deadline only

* **Description:** offer `--stop-after` and leave signals with their default behaviour.
* **Why rejected:** Ctrl-C at a terminal would still throw the whole run away.

### Alternative 4 — Exit `2` for a stopped run

* **Description:** treat a run that did not reach its end as a run that could not complete.
* **Why rejected:** `2` means the tool did not work. A nightly run that meets its deadline would be
  reported as broken every night.

### Alternative 5 — Exit `130` or `143` when a signal stopped the run

* **Description:** follow the shell convention of 128 plus the signal number.
* **Why rejected:** it is a new code for a cause an existing code already names, and a deadline has
  no signal number, so one outcome would carry two codes depending on what ended it.

### Alternative 6 — Print a score over the mutants judged

* **Description:** report the usual percentage, computed over whatever the run reached.
* **Why rejected:** those mutants are the front of the run, not a sample, so the number would
  describe the order projects were found in.

## Consequences

### Positive

* A run that outlasts its CI budget reports what it judged instead of nothing.
* Ctrl-C keeps the work done so far, and a second Ctrl-C still ends the process at once.
* A run whose gate was asked for never reports a pass it did not establish, however it ended.

### Negative

* A stopped run with no gate exits `0`, so a nightly run that reaches its deadline every night stays
  green; the only signs are a line on standard error and `complete: false` in the report.
* The deadline has to be set below the CI budget by hand, with a margin for the mutants in flight
  and for writing the report. The tool cannot know the budget it is running under.
* Because the order is fixed, a run that keeps stopping keeps judging the same front, and the
  projects found last are never reached however many times it runs.

### Risks

* A consumer of the JSON report that reads `hasUndetected` without reading `complete` may take a
  stopped run that found nothing yet for a pass.
* A host killed by the same signal as the tool could, in principle, be seen to end before the stop is
  recorded. The stop is requested as the signal lands while a host's end is noticed only after the
  host has handled it, so its verdict should be discarded, but that rests on ordering rather than on
  a guarantee.

### Follow-up actions

* `Reefact/slugger`'s workflow can pass `--stop-after` below its `timeout-minutes`, which is the case
  that prompted this record.
* What survives a kill the tool never hears about is not settled here; see
  [DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md).
