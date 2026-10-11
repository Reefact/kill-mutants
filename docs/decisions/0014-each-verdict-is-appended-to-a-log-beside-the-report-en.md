# DEC0014 | Each verdict is appended to a log beside the report as soon as it is final

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-11 | Proposed | First draft | |
| 2026-10-11 | Accepted | | |

## Context

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md) lets a run that is
stopped write its report - but only a stop the run sets itself or hears. Some ends reach it as
neither. On GitHub Actions a job cancelled by a person, or by `concurrency: cancel-in-progress` when a
newer push arrives - which this repository's own CI uses - is ended the way a timeout is: a signal to
the step's shell, then the process tree killed seconds later. A runner that is lost ends everything
with no signal at all. And a run that meets its own deadline still has its mutants in flight and its
report to write, so a deadline set too close to the CI budget can be overtaken by the kill.

What survives a process that is killed is what it had already handed to the operating system. Before
this decision that was nothing: every verdict stayed in memory until the report was written at the
end.

The JSON report is read by CI steps after the run.
[Issue #24](https://github.com/Reefact/kill-mutants/issues/24) quotes `Reefact/slugger`'s summary
step, guarded by `hashFiles('killmutants-report.json') != ''` so that it is skipped when the run
wrote no report. The report's `complete` field is new with DEC0013, so no consumer written before it
reads it. Its `hasUndetected` field is what a consumer gating on the report reads to decide whether
a partial run passed. [DEC0009](0009-exit-codes-are-a-public-contract-en.md) names the dangerous
direction for a misread outcome as the silent one, where a run is taken for a pass it never was.

A mutant's status can change after it is first seen in two ways only. A timeout is re-run alone,
once the workers have finished, and may come back as something else. A kill sampled by
`--verify-kills` is tested again, which never changes its status but can add a disagreement beside
it. Every other status is final as soon as the mutant has been tested.

The maintainer asked that whatever is written be named from the `--report-json` path rather than
configured with an option of its own.

## Decision

When a run writes a JSON report, it also appends each verdict, as soon as it is final, as one line of
JSON to a log beside the report that takes the report's name with the extension `.jsonl`.

## Rationale

Only what is on disk outlives a kill, so a verdict has to reach the disk when it is reached rather
than when the run ends. That is the whole case for a log, and it holds for every end DEC0013 cannot
see.

A file of its own rather than the report rewritten as the run goes. Every intermediate version of a
rewritten report would sit where a finished one is expected, read by consumers that do not know
`complete` exists - and on a partial run, `hasUndetected: false` in a report half written reads as a
pass. That is the silent direction DEC0009 is about. With a separate log, the report file only ever
holds a finished run or one that says it stopped.

Only final verdicts, so that a line, once written, is never contradicted by a later one. A log that
recorded every status as first seen would have to be read "last line per mutant wins", and a consumer
that read it any other way would count an unconfirmed timeout as a detection. Holding a timeout back
until it is confirmed costs a few lines at the end of each project; a disagreement found by
`--verify-kills` stays in the report, beside the verdict it qualifies.

One line in the shape of an entry of the report's `mutants`, so a consumer that already reads the
report reads the log with the same code, and the two never name a mutant differently.

Named after the report, as the maintainer asked: a job that already names its report has its log
without asking, and a reader who finds one file knows where the other is.

## Alternatives considered

### Alternative 1 — No log

* **Description:** rely on DEC0013's stop alone, and write verdicts only in the report.
* **Why rejected:** it covers only an end the run sets or hears. A cancelled job, a lost runner and a
  deadline overtaken by the kill would still leave nothing.

### Alternative 2 — Rewrite the JSON report as the run goes

* **Description:** write the report every few seconds, atomically, marked `complete: false` until the
  last write.
* **Why rejected:** every intermediate file sits where a finished report is expected, and a consumer
  that does not read `complete` takes a partial run that has found nothing yet for a pass.

### Alternative 3 — Log every status as first seen

* **Description:** write a line whenever a mutant's status is first known, and another when a timeout
  is confirmed or a kill is re-verified, the later line replacing the earlier.
* **Why rejected:** every consumer would have to apply "last line wins", and one that did not would
  count unconfirmed timeouts as detections.

### Alternative 4 — An option of its own for the log's path

* **Description:** a `--verdicts <path>` option and a matching key in `killmutants.json`.
* **Why rejected:** the maintainer preferred the path derived from `--report-json`, which needs no
  new setting.

## Consequences

### Positive

* A run killed at any point leaves every verdict it had reached behind it.
* The log can be followed while the run goes on, and read by a step that runs after a cancelled one.
* There is one shape for a mutant's verdict, in the report and in the log.

### Negative

* Every run that writes a JSON report leaves a second file beside it, emptied when the next run
  starts.
* A report path ending in `.jsonl` is refused, since it would be the log's.
* The log does not say how many mutants the run had selected, whether it finished, or what
  `--verify-kills` found: those are the report's. A log without a report beside it is a run that did
  not finish, and that is all it can say about itself.
* Lines are in the order verdicts landed, which depends on which worker finished first.

### Risks

* A reader may take the log for the run's result and draw from it the conclusions only a finished
  report supports.

### Follow-up actions

* `Reefact/slugger`'s summary step can read the log when the report is missing, instead of being
  skipped.
