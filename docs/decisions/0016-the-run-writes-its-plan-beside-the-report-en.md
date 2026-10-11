# DEC0016 | The run writes its plan beside the report, as a file of its own

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-11 | Proposed | First draft | |
| 2026-10-11 | Accepted | | |

## Context

The maintainer asked for a run that writes its plan when it starts, so that at any later moment what
was done and what remains can both be read — by a later run that continues the series
([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md)), and
by whoever reads what a killed run left behind.

[DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md) appends one line per final
verdict to a JSON Lines log named from the report, and counts among its positive consequences that
*"there is one shape for a mutant's verdict, in the report and in the log"*: every line is an entry of
the report's `mutants`, and a consumer reads both with the same code. The repository's own tests read
the log that way — `key` on every line, and as many lines as verdicts told. DEC0014 also records that
the log *"does not say how many mutants the run had selected, whether it finished, or what
`--verify-kills` found: those are the report's"*, and that the maintainer preferred the log's path
derived from `--report-json` over an option of its own. A report path ending in `.jsonl` is refused,
because that name is the log's.

The whole population of a run is known at one moment: once every project's compilation is in memory
and every mutant has been generated, before any is tested, when `VerdictLedger.Select` is called. A
stopped run already uses that moment to say how many mutants it had selected. What a run knows grows
afterwards: which run reached which verdicts, and the disagreements `--verify-kills` finds, which
`VerdictLedger.Amend` records without telling any observer, so that they live in the report and never
in the log.

A file that is read whole can be rewritten atomically, by writing a new copy and moving it over the
old one. An append-only file cannot be rewritten that way, and a line that is not a verdict in it would
have to be skipped by every reader.

GitHub Actions kills a job's process tree seconds after signalling the step's shell, so a run can end
with its log written and no report; a summary step that runs afterwards can read only what is on disk.
Today it can read the verdicts reached and nothing about what they were a part of.

## Decision

In this context, every run that writes a JSON report also writes, beside it and named from it with
the extension `.plan.json`, a plan that identifies the run and lists every mutant it selected, so that
the log keeps holding verdicts and nothing else.

## Rationale

The plan is the document the maintainer asked for: the list of what a run set out to judge, with what
identifies the code and the settings it set out to judge it under, in the report's own field names so
that a line of the log joins a row of the plan on the key. Plan minus log is the remainder, readable
by a later run and by a summary step alike, with the tool or without it.

A file of its own rather than a first line in the log, because DEC0014's one shape is a property worth
keeping: every reader of the log, this repository's tests included, takes each line for a verdict, and
a header would make every one of them skip a line. The plan is also rewritten as the run learns — the
run that reached a verdict, a disagreement found later — and a document is rewritten whole and
atomically, which an append-only log cannot be.

Named from the report, as the log is, because the maintainer preferred derivation to an option and a
reader who finds one of the three files knows where the other two are; and `.plan.json` is refused as
a report name for the reason `.jsonl` already is.

Written on every run that writes a report, not only on one that names a series, because a killed
run's remainder is worth reading even when nobody continues it, and only a plan says what the log's
lines were a part of.

## Alternatives considered

### Alternative 1 — A header line in the verdict log

* **Description:** write the plan as the first line of the `.jsonl`, in a shape of its own.
* **Why rejected:** every line of the log would no longer be a verdict, which breaks DEC0014's one
  shape and every reader written to it, and a header cannot be rewritten as the run learns.

### Alternative 2 — No plan: regenerate it

* **Description:** a later run regenerates the population, which is deterministic, and compares it to
  the log; nothing is written at start.
* **Why rejected:** a summary step after a killed run has nothing to read what remains from, and the
  identity a resumed run needs would have nowhere to live but the log's lines.

### Alternative 3 — Write the plan only when `--resume` is given

* **Description:** a plain run writes report and log as today; only a run that names a series writes a
  plan.
* **Why rejected:** a run that names no series can be killed too, and its remainder is unreadable
  without a plan.

### Alternative 4 — An option naming the plan's path

* **Description:** `--plan <path>`, with a matching key in `killmutants.json`.
* **Why rejected:** the maintainer preferred the log's path derived from the report's, and the same
  reason holds here: a job that already names its report has its plan without asking.

## Consequences

### Positive

* What a run set out to judge, and under what identity, is on disk before the first mutant is tested,
  and survives a kill.
* The remainder of a killed run is readable from plan and log alone.
* The log keeps holding verdicts and nothing else.

### Negative

* Every run that writes a report leaves a third file beside it.
* A report path ending in `.plan.json` is refused.
* The plan is rewritten several times per run, and a rewrite that fails during a stop is put down to
  the stop.

### Risks

* A reader may take the plan's list for the run's result rather than for what it set out to judge.

### Follow-up actions

* Have `Reefact/slugger`'s summary step read plan and log when the report is missing, and say how many
  mutants remain.
