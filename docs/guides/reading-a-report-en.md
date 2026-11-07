# Reading a report

🌍 **Languages:**  
🇬🇧 English (this file) | 🇫🇷 [Français](./reading-a-report-fr.md)

A run writes two things: progress, for whoever is watching, and a report, for whoever has to act on
it. This page goes through both, then through the JSON report a script reads, the files a run
leaves beside it, and the exit codes.

## Two streams, on purpose

Progress goes to **standard error** and the report to **standard output**, so
`killmutants > report.txt` captures the report without a progress line threaded through it. What
standard error shows is chosen with `--progress`; nothing about the terminal is detected
([DEC0023](../decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-en.md)):

| Mode | For | What it shows |
| --- | --- | --- |
| `log` — the default | a CI log, a file, a pipe, and anyone who asked for nothing | dated lines: each phase once, then its count and the mutant in progress at most once a minute |
| `live` | a person at a terminal | one line rewritten in place: the phase, the count, a bar, the time left and the verdicts so far |
| `none` | a tool that reads the JSON report | nothing but the parts set aside |

Two events are written the moment they happen in every mode but `none`: a part of the run set aside,
with its reason, and a mutant still running after `--slow` (30 seconds by default), once, with the
budget it was given. These lines are for people and may change between versions; a tool reads
`--report-json`.

## The console report, section by section

```text
KillMutants

Survived (2)

  Shipping.cs
     7:16  orderTotal >= 100m    -> orderTotal > 100m     [Comparison]
    12:38  weightInGrams <= 2000 -> weightInGrams < 2000  [Comparison]

No coverage (5)

  Labels.cs
    7:52  "Free shipping"      -> ""   [StringLiteral]
    7:70  "Standard shipping"  -> ""   [StringLiteral]
    ...

Mutants: 13
Killed: 6
Survived: 2
No coverage: 5

By mutator

  Comparison       5 mutants, 2 detected  (40%)
  Conditional      4 mutants, 2 detected  (50%)
  StringLiteral    2 mutants, 0 detected  (0%)
  LogicalOperator  1 mutant, 1 detected  (100%)
  NegatedDecision  1 mutant, 1 detected  (100%)

Mutation score: 46.15%
Elapsed: 11.1 s
Ran on: .NET 10.0.14 on Ubuntu 24.04.5 LTS, 2 of 4 processors, xUnit 4.0.0, timeout budget 31.2 s (0.4 s baseline alone × 3 + 30 s), no kills re-verified
```

### Caveats come first

Anything that changes how the rest should be read is printed **before** it, so a reader who stops
early cannot take a number away without the sentence that qualifies it. A clean run has none. The
four that exist:

* **Stopped before the end** — the run hit `--stop-after` or was interrupted. The verdicts below
  hold; the mutants without one were never asked about.
* **Set aside (n)** — a part of the run could not be measured: a project MSBuild could not read, a
  test project that did not build, a red baseline. Each is named with its reason. No gate passes
  on such a run, and it exits `2`.
* **Series '…'** — this report sums several runs of a [series](./continuous-integration-en.md#finishing-a-large-project-over-several-runs),
  and says how many verdicts came from earlier runs.
* **Comparison incomplete** — a [partial run](./pull-requests-en.md) could not fully read the
  earlier state, and withholds its verdict.

### Findings

**Survived** and **No coverage** list the undetected mutants, grouped by file and ordered by
position. Each line is `line:column`, the original code, the mutated code, and the family. Long
texts are cut at sixty characters with an ellipsis; the full text is in the JSON report.

### Counts

`Mutants`, `Killed`, `Survived` always; `Timed out`, `Compile errors`, `No coverage` and
`Ignored` only when they happened, so a clean run stays short.

### By mutator

What each family produced, what your tests caught, and the ratio — shown when more than one family
ran. This is the one number to act on directly: a family producing a third of the mutants and
detecting a tenth of them is either the most valuable finding in the report or the least, depending
on the project. [The mutators](./mutators-en.md#choosing-a-catalogue) says how to act on it.

### Ignored by the project's rules

Present only when `--ignore-calls` is in use: each rule with the mutants it silenced, and a rule
that silenced nothing said out loud — that one is a renamed method or a pattern that never matched.

### Warnings

Lines prefixed with `!`, placed immediately before the score so they cannot be missed: a verdict
that did not repeat under `--verify-kills`, and the like. They are also in the JSON report, so a CI
job can act on them.

### The last three lines

**Mutation score** — detected over detected plus undetected, with the ignored count beside it when
there is one. A partial or stopped run prints a sentence here instead, saying why there is no score.

**Elapsed** — wall-clock time of the whole run.

**Ran on** — runtime, operating system, workers used out of processors available, xUnit version,
the timeout budget as the formula it is, and how many kills were re-verified. Two reports without
this line are not comparable, and nobody notices.

## Re-verifying kills

A flaky or order-dependent test produces a kill the mutation did not cause. `--verify-kills <n>`
tests `n` of the mutants reported killed a second time, each on its own, and reports any verdict
that does not survive its own repetition — as a warning, and on the mutant itself in the JSON
report. It costs one test run per sampled mutant, so it is off unless asked for.

## The JSON report

`--report-json <path>` writes the same report for anything that is not a person: a CI summary, a
dashboard, a diff between two runs. It is deliberately flat, and it names every mutant with a
stable identity and its exact position rather than summarising.

```json
{
  "tool": "KillMutants",
  "score": 0.4615,
  "scoreDisplay": "46.15%",
  "durationSeconds": 11.15,
  "scope": { "mode": "full", "comparedFrom": null, "comparedTo": null, "comparedToIsExact": true, "changedFiles": 0 },
  "environment": { "runtime": ".NET 10.0.14", "operatingSystem": "Ubuntu 24.04.5 LTS", "processorCount": 4, "workerCount": 2, "testFramework": "xUnit 4.0.0", "timeoutBudgetSeconds": [31.21], "baselineSecondsAlone": [0.4], "killsReVerifiedAlone": 0, "timeoutBaselineFactor": [3], "timeoutMarginSeconds": [30] },
  "totals": { "mutants": 13, "killed": 6, "survived": 2, "timedOut": 0, "compileErrors": 0, "noCoverage": 5, "detected": 6, "undetected": 7, "untestable": 0, "ignored": 0 },
  "ignoredCalls": [],
  "hasUndetected": true,
  "inconclusive": false,
  "complete": true,
  "mutantsSelected": 13,
  "setAside": [],
  "series": null,
  "coverageLost": [],
  "widened": [],
  "undetectedFromWidening": null,
  "comparisonIsIncomplete": false,
  "unreadComponents": [],
  "warnings": [],
  "byMutator": [ { "mutator": "Comparison", "mutants": 5, "detected": 2, "undetected": 3, "untestable": 0, "ignored": 0, "score": 0.4 }, "..." ],
  "mutants": [ "..." ]
}
```

| Field | Meaning |
| --- | --- |
| `score`, `scoreDisplay` | the score as a ratio and as printed; **`null`** on a partial or stopped run, and when nothing could be tested — never a zero that was not measured |
| `scope` | which population was inspected: `full`, or `partial` with the two states compared and how many files changed |
| `environment` | everything behind `Ran on`, as data |
| `totals` | the counts by status, plus the three that define the score: `detected`, `undetected`, `untestable`; and `ignored`, outside all three |
| `hasUndetected`, `inconclusive` | the binary answer a partial run gives; both meaningful on a full run too |
| `complete`, `mutantsSelected` | whether the run judged everything it selected; `complete` is `false` on a stopped run and on one that set something aside |
| `setAside` | what could not be measured, with its stage and reason |
| `series` | `null`, or the runs this report sums |
| `coverageLost`, `widened`, `undetectedFromWidening`, `comparisonIsIncomplete`, `unreadComponents` | what a [partial run](./pull-requests-en.md) found about the change itself |
| `warnings` | the `!` lines, as text |
| `byMutator` | one entry per family that ran; `score` is `null` where the run's own is |
| `mutants` | one entry per mutant, below |

Each entry of `mutants` carries what a verdict needs to be argued with:

```json
{
  "key": "4a420df55ffb8fef",
  "id": "M8",
  "mutator": "Comparison",
  "status": "Survived",
  "outcome": "Undetected",
  "path": "Shop/Shipping.cs",
  "file": "/home/you/shop/Shop/Shipping.cs",
  "line": 7,
  "character": 16,
  "original": "orderTotal >= 100m",
  "mutated": "orderTotal > 100m",
  "ignoredBy": [],
  "killedBy": [],
  "disagreement": null,
  "overturned": null,
  "detail": null
}
```

* **`key`** is derived from what the mutant *is* — file, position, family, both texts — so two
  reports of the same commit can be joined on it even though the `M8`-style `id` shifts between
  runs.
* **`killedBy`** names the tests that failed, in the form the runner accepts, so a doubted kill can
  be settled by hand: put the mutation in the file at that position, run those tests, watch them
  fail.
* **`disagreement`** and **`overturned`** are set only on a mutant `--verify-kills` re-tested whose
  verdict did not repeat.

## The files beside the report

A run that asked for `--report-json artifacts/mutation.json` also writes two files next to it. Both
exist for the run that never gets to write its report: a job cancelled by a newer push, a runner
that was lost.

**`artifacts/mutation.jsonl` — the verdict log.** One line per mutant, in exactly the shape of the
report's `mutants` entries, appended the moment the verdict is final. A report an earlier run left
at that path is cleared when the run starts, so a log with no report beside it is always a run that
did not finish ([DEC0014](../decisions/0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md)).

**`artifacts/mutation.plan.json` — the plan.** What code and settings the run measured, and every
mutant it selected, so that *plan minus log* is what remains after a killed run, readable without
the tool. It is written before anything is discovered, with the mutant list still `null`, and
rewritten whole once the mutants are selected
([DEC0016](../decisions/0016-the-run-writes-its-plan-beside-the-report-en.md)).

A report path ending in `.jsonl` or `.plan.json` is refused, because those names are taken.

## Exit codes

Exit codes are a public contract: scripts encode them, so a code is never renumbered, and a new
cause of an outcome a code already names joins it rather than taking a code of its own
([DEC0009](../decisions/0009-exit-codes-are-a-public-contract-en.md)).

| Code | Meaning |
| --- | --- |
| `0` | Ran, and the gate you asked for passed — or you asked for none |
| `1` | Ran, but the gate did not pass: the score is below `--break-at`, no mutant could be tested at all, `--since` found an undetected mutant in the change, or the run was stopped before the gate could be shown to pass |
| `2` | Could not run, or ran with part of its scope set aside; the report or standard error says why |
| `64` | The command line was not understood |

The distinction that matters is between `1` and `2`: *your tests are weaker than you asked for*
and *the tool could not do its job* call for different reactions, and a job that cannot tell them
apart will eventually treat a broken environment as a quality regression — or the other way round.
Standard error always says which case it was:

```text
Mutation score 46.15% is below the 80% threshold.
```

## Where to go next

| If you want to… | Read |
| --- | --- |
| understand what the statuses and the score mean | [Core concepts](./core-concepts-en.md) |
| act on the per-family split | [The mutators](./mutators-en.md#choosing-a-catalogue) |
| read the extra sections a partial run prints | [Judging a pull request](./pull-requests-en.md) |
| use the log and the plan across several CI jobs | [Continuous integration](./continuous-integration-en.md) |

---

[← Documentation index](../README.md)
