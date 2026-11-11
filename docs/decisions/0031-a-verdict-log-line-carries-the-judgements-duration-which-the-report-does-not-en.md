# DEC0031 | A verdict log line carries how long its judgement took, which the JSON report does not

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-10 | Proposed | First draft | |
| 2026-11-11 | Accepted | At the maintainer's request | |

## Context

DEC0014 appends each final verdict to a log beside the report, as one line of JSON "in the shape of
an entry of the report's `mutants`", so that a consumer reading the report reads the log with the
same code; its consequences say there is one shape for a mutant's verdict, in the report and in
the log. The line is written as soon as the verdict is final, so a run that is stopped or killed
keeps every line it wrote.

Nothing said how long a mutant took to judge. Running KillMutants on `Reefact/just-dummies`, about
5,600 mutants in a run of well over an hour, the only figure available was a mean derived from the
rate of verdicts. It hides the spread: a killed mutant stops at the first failing test, a survivor
runs every test of its selection, and a mutant with no coverage costs almost nothing. A duration
per mutant shows which mutants and which files are slow, lets the timeout be tuned from
measurements rather than from a multiple of the baseline, informs the number of workers, and
estimates what a run has left (#75).

The JSON report is deterministic: two runs on the same code write the same report. A duration
changes at every run.

A mutant can be judged more than once. A timeout is believed only once a second run, alone,
reproduces it (RB-013), and `--verify-kills` re-tests a sample of kills without changing their
verdict. Workers judge side by side, so durations overlap: their sum is larger than the run's
wall-clock time.

The maintainer settled the shape in #75 and its review: the duration goes in the verdict log only,
not in the report; no new option and no separate file; one line in the final console summary, with
the median and the slowest mutant and no total; a verdict inherited from an earlier run keeps its
duration or has none; resume and the run's identity ignore the field. On 2026-11-10 the maintainer
chose that the duration recorded is that of the judgement which produced the final verdict.

## Decision

In this context, we decide that each line of the verdict log carries, beside the report entry it
already holds, the duration of the judgement that produced its verdict, and that the JSON report
carries none.

## Rationale

The log is the one place a duration can be both kept and harmless. It is written as each verdict
becomes final, so a stopped or killed run keeps its durations, which is exactly the run whose time
someone wants to understand. The report is what two runs on the same code agree on; a field that
changes at every run would make every diff and every versioned report noisy, for a figure nobody
compares between runs.

The line keeps the report's entry whole and adds to it, so DEC0014's purpose survives: a consumer
reads the mutant with the same code in either file, and a line with no duration is still exactly
an entry. What changes is the letter of "one shape": the log's shape is the entry plus what only
the log carries.

Recording the judgement that produced the final verdict makes the median and the slowest measure
what one judgement costs. A confirmed timeout's duration is its confirmation's, run alone, rather
than that plus a first pass spent under load; a re-verified kill keeps the duration of the
judgement that reached it, since the re-test changes no verdict. Durations overlap between workers,
so the console gives a median and a slowest and never a sum, which would read as the run's time.

## Alternatives considered

### Alternative 1 — No duration per mutant

* **Description:** keep the only figure available today, a mean derived from the rate of verdicts.
* **Why rejected:** a mean hides the spread between kills, survivors and uncovered mutants, which is
  what tells where the time goes.

### Alternative 2 — The duration in the JSON report as well

* **Description:** add the same field to each entry of the report's `mutants`.
* **Why rejected:** the report would no longer be the same for two runs on the same code. If it is
  ever wanted there, it is an explicit option and a decision of its own.

### Alternative 3 — A separate timings file, or an option that writes one

* **Description:** write durations to a file of their own, always or behind a flag.
* **Why rejected:** another file and another option are another contract to keep, for little: the
  log is already read by script.

### Alternative 4 — The sum of every judgement of a mutant

* **Description:** record, for a mutant judged more than once, the total of its judgements.
* **Why rejected:** the maintainer chose the judgement that produced the final verdict; a sum would
  count a confirmed timeout's budget twice and make the slowest mutant a measure of the
  confirmation rule rather than of the mutant.

## Consequences

### Positive

* Each judged mutant's cost is on disk as soon as its verdict is, including for a run that never
  writes its report.
* The JSON report stays the same for two runs on the same code.
* The console summary says where a long run's time goes, in one line.

### Negative

* A log line is no longer exactly its report entry: a consumer comparing the two has to leave
  `durationMs` aside.
* Two runs on the same code write different log lines.
* A verdict inherited from a line written before this change has no duration, and the summary is
  then over part of the verdicts.

### Risks

* A reader adds the durations up and takes the sum for the run's time.
* A duration measured on a loaded machine is read as the mutant's own cost.

### Follow-up actions

Not recorded at the time of the decision.
