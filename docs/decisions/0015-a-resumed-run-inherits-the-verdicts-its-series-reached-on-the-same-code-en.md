# DEC0015 | A resumed run inherits the verdicts its series reached on the same code

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-11 | Proposed | First draft | |
| 2026-10-11 | Accepted | | |

## Context

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md) lets a run stop at a
deadline or on a signal and report the verdicts it reached, with no score and no gate passed.
[DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-en.md) appends each final verdict,
as it lands, to a JSON Lines log beside the JSON report, so a run that is killed outright still leaves
what it judged. Both stop at the end of one run. DEC0013 records the consequence in so many words:
*"Because the order is fixed, a run that keeps stopping keeps judging the same front, and the projects
found last are never reached however many times it runs."* On `Reefact/slugger`
([issue #24](https://github.com/Reefact/kill-mutants/issues/24)) the engine alone holds 816 mutants
and takes 88 minutes on four cores with four workers, against a CI budget of 90 minutes for the whole
job. A run stopped at 80 minutes every night judges the same 80 minutes of it every night.

The maintainer asked for a run that writes its plan when it starts, records each verdict as it lands
so that what was done and what remains can both be read, stops at its deadline, and lets a later run
pick up the remainder, with one consolidated and complete report at the end. The maintainer also asked
that the series be named by an identifier the caller passes — the commit hash, typically — rather
than inferred by the tool.

What the code offers to build that on, and what it does not:

* A mutant carries two identities. `MutantId` is a counter, worthless across runs. `MutantKey` is a
  hash of the file path relative to the directory the run was pointed at, the line, the character, the
  mutator family and the text before and after the mutation; it is the same in every run of the same
  code, and it is published in the report and in the log for exactly that purpose. It says nothing
  about anything else: not the rest of the file, not the other files, not the tests, not the build
  configuration.
* Generation is deterministic for one checkout. Projects are discovered in ordinal path order, and
  files are walked in the order MSBuild's compiler command line lists them. Nothing pins that order
  across machines, so two runs of the same code can number the same mutants differently while giving
  them the same keys.
* A verdict depends on things no key sees. A commit that touches only tests changes which mutants are
  killed. A change outside every mutation site — a project file, a package reference, a line after the
  last mutable expression — changes what is built. `--no-coverage` turns `NoCoverage` into `Survived`.
  An `--exclude` pattern removes a project from the test side as well as from the mutable side, and a
  list given on the command line replaces the file's. The tool's own version changes how a verdict is
  reached.
* `KillMutants.Git` already resolves `HEAD` to a commit and detects an edited working tree, with
  `git status --porcelain`, for the partial run; the report records both as `comparedTo` and
  `comparedToIsExact`. `git status` lists untracked files, so a file the tool itself writes beside the
  report counts as an edit unless the read excludes it. The core never calls git: it receives names
  and directories through `IChangeSource`, and the dependency runs one way on purpose.
* A timeout becomes final only once it has been re-run alone, under a budget derived from a baseline
  measured on that machine. The report publishes a budget only for the projects the run itself
  baselined, and `TimeBudget` and `RunEnvironment` argue that a timeout nobody can explain from the
  report is the failure those fields exist to prevent.
* A test host that dies without writing a result file reads as a crash, and a crash counts as a kill,
  because the baseline proved the host runs cleanly unmutated. A verdict reached while the run is
  coming down is discarded, but DEC0013 records that this rests on ordering rather than on a guarantee;
  a process tree killed by a CI runner never reaches the tool at all.
* `--verify-kills` re-tests a sample of the kills in the list it is handed. It never changes a status;
  a disagreement is recorded with `VerdictLedger.Amend`, which tells no observer, so it exists in the
  report and never in the log. This repository re-verifies ten kills on every run, and
  `RunEnvironment` notes that a run which verified none of its kills and one which verified all of them
  both read "no disagreements".
* Every verdict becomes final in one place, `VerdictLedger`, which records what the run selected and
  what it reached; a report is built from the ledger alone.
* `RunEnvironment` already records the xUnit version read from the built test assemblies, and
  discovery pairs each project under test with the test projects that reach it, directly or through
  another project. Judging a mutant again costs one test run, or one budget when it times out again.
  On this repository's own run of 7 October 2026, one mutant in 797 timed out.
* The exit codes are a public contract ([DEC0009](0009-exit-codes-are-a-public-contract-en.md)): a
  new cause of an outcome a code already names joins that code. A partial run has no score
  ([DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md)); DEC0010 names inheriting results
  across commits for unchanged mutants and tests as *incremental computation*, a different feature.
  A stopped run has no score because the mutants it judged are the front of the run, not a sample.
* On GitHub Actions each job starts from a fresh workspace, so anything that must outlive a job
  travels as an artifact or a cache entry. A scheduled run checks out the tip of its branch, which
  moves between nights. A pull-request run checks out a synthetic merge commit whose hash changes
  whenever the base branch moves, even with no new commit on the pull request. The hosted runner
  images are updated between scheduled runs.

Three designs were confronted before this record was drafted — the plan-and-resume the maintainer
described, a stateless split of the population into shards merged afterwards, and a resume driven by
the log alone — each with an adversarial review. The shards write no plan and repeat the fixed-order
defect inside each shard; the log alone has nowhere to write the plan the maintainer asked for, and a
header line in it would break DEC0014's rule that every line of the log is a verdict. The reviews also
found that a resume keyed on a clean tree can never trigger if the tool's own files count as edits,
and that a kill the stop itself caused can reach the log.

The maintainer arbitrated: a timeout is never inherited; disagreements found by earlier runs are
carried; resumption is asked for explicitly on the command line, with no key in `killmutants.json`
and no `--since` series in the first version; and this decision and the plan file are two records.

## Decision

In this context, a run given `--resume <id>` continues the series of that name — inheriting, from
the plan and log beside its report, every final verdict an earlier run of the series reached on the
same commit, clean tree, settings and tool, except timeouts and kills no test is named for — judges
only the mutants that still have none, and reports a score only once every selected mutant has a
verdict.

## Rationale

A series is named by the caller because the caller is the one who knows what it is. A workflow that
passes the commit hash makes one series per commit without a condition anywhere; a run that finds no
plan of that name starts the series, and one that finds it continues it. Inferring the series from a
plan that happens to sit beside the report would make a nightly on a quiet branch stop re-measuring
without anyone having asked, and would turn the report's claim — "the sum of several runs" — into
something a job gets without knowing.

The name is not evidence. A kill reached against tests this run does not contain is the worst thing
this tool can print, and a key cannot see the tests, the configuration, the exclusions or the rest of
the code. So the name opens the series and the code state decides whether it may continue: the same
commit with a clean tree is the identity the report already stands behind for a partial run, and the
settings, the tool's version, the population actually generated, the project graph and the xUnit
version read from the built assemblies are all things the run computes anyway, so comparing them
costs nothing. A difference under the same name is refused with the difference named, rather than
silently starting over — the log may hold hours of verdicts, and emptying it for a cause the user did
not create is not the tool's call. A different name, or no plan, is a new series, which is what lets a
workflow always pass the same flag.

The tree is read as clean with the tool's own three files left out, because they are written beside
the report before the run and a CI job restores them there; counted as edits, they would make the
series unable to continue in the one deployment it exists for. Runtime, operating system and processor
count are recorded for every run and compared by nobody: a runner image rolls forward between two
nights of a pinned series, and blocking on it would leave the series stuck under a name nobody can
change by committing.

A timeout is judged again rather than inherited because it is final only under the budget of the run
that confirmed it, and the report can only publish the budgets this run measured. Re-judging costs one
budget per timeout and keeps every timeout in a consolidated report explainable from that report. A
kill with no test named is judged again because a crash is the one verdict the stop itself can
manufacture, the discard that protects against it rests on ordering, and a process tree killed by a
runner is never heard. Judging it again costs one run per such mutant.

The score is legitimate on a completed series where DEC0013 refused it: the population is every
selected mutant of the codebase at one named state, judged in several runs, which is the codebase and
not the front of anything. Until then an intermediate run is exactly a stopped run, and DEC0013's
codes apply unchanged — `1` when a gate was asked for, `0` when none was — because a gate not yet
shown to pass is the truthful reading, and a gated run exiting `0` without having established the
answer is the silent direction DEC0009 forbids.

Disagreements travel in the plan rather than in the log because the log holds verdicts and nothing
else, and `Amend` already records without announcing. The run that completes a series spreads its
`--verify-kills` sample over the inherited kills of the projects it still baselines, because a report
that prints a score beside "no kills re-verified" is the state `RunEnvironment` calls meaningless.

`--since` series wait for a record of their own: their identity would be the compared revisions, and
on a pull request the checked-out merge commit moves whenever the base does, so the case needs its
own facts rather than a footnote here.

## Alternatives considered

### Alternative 1 — Keep DEC0013 and DEC0014 as they stand

* **Description:** a stopped run reports what it judged and leaves its log; the next run starts over.
* **Why rejected:** a project larger than one budget is never finished, and every night judges the
  same front — the consequence DEC0013 wrote down and this record exists to remove.

### Alternative 2 — Resume implicitly whenever a matching plan sits beside the report

* **Description:** no flag; a run that finds a plan for the same code state continues it.
* **Why rejected:** a nightly on an unchanged branch would silently stop re-measuring, which defeats
  what `selfcheck.yml` says it is for — a kill that stops reproducing — and a report that is the sum
  of several runs is a claim a job should make knowingly.

### Alternative 3 — Inherit across commits the verdicts whose mutant and reaching tests are unchanged

* **Description:** compare the mutated site and the tests that reach it between two commits, and keep
  the verdict when both are the same.
* **Why rejected:** it is the incremental computation DEC0010 set aside, with an identity problem of
  its own — what "unchanged" means for a test that calls into changed code — and building it under the
  name of a resume would mix two populations in one report.

### Alternative 4 — Split the population into shards and merge their reports

* **Description:** `--shard i/n` judges a deterministic share of the mutants in each of n parallel
  jobs, and a merge step consolidates the n reports.
* **Why rejected:** it writes no plan and keeps no remainder, so a shard stopped at its deadline every
  night repeats the fixed-order defect inside its share; and its merge scores on a fingerprint of the
  mutation sites, which cannot see a test change, a configuration or an exclusion. It answers how to
  go faster, not how to finish.

### Alternative 5 — Inherit timeouts and carry each run's budgets in the plan

* **Description:** keep a confirmed timeout from an earlier run, and record the budget it was judged
  under beside it.
* **Why rejected:** the consolidated report's budgets would then describe only the run that wrote it,
  and a reader would find the budget behind an inherited timeout only through a second structure.
  One mutant in 797 timed out on this repository's own run, and judging one again costs one budget.

### Alternative 6 — Rotate the order so that every project is reached over several nights

* **Description:** start each run where the previous one stopped, without inheriting anything.
* **Why rejected:** a project larger than one budget is still never finished, and no run ever has a
  population a score can describe.

## Consequences

### Positive

* A project that does not fit in one CI budget is finished over several runs, and the projects found
  last are reached.
* A completed series has a score and answers a gate, over every selected mutant of the codebase at
  one named state.
* A run killed outright leaves its plan and its log, so what remains can be read without the tool.

### Negative

* A series lives on one commit with a clean tree. A nightly that follows a moving branch never
  completes one unless its workflow pins the checkout to the series' commit until the series is done.
  A dirty tree never resumes, even with identical content.
* A series with `--break-at` exits `1` on every intermediate run, by DEC0013's rule.
* A resumed run repays discovery, build, analysis, and the baseline and coverage of every project that
  still has a remainder. A project whose timeouts are never confirmed before the deadline judges them
  again on every run.
* A run that completes a series may itself run few tests, since a project with nothing left is neither
  baselined nor measured; the report says how many verdicts it inherited and from how many runs.
* `--resume` needs `--report-json`, because the series lives beside the report, and a git repository,
  because the code state is read from it.
* A run at the same report path that names no series, or another one, replaces the plan and empties
  the log: a series shares its report path with nothing else.
* Two mutants that share a key are both judged again, since a log line cannot say which one it is
  about.

### Risks

* A kill produced by a flaky, order-dependent or machine-sensitive test in one run is inherited by the
  rest of the series; only that run's own `--verify-kills` sample and the completing run's sample over
  inherited kills can catch it.
* The commit identifies the sources, not the restored dependencies: a floating package version or a
  rolled-forward SDK can change the tests between two runs of one commit. The xUnit version read from
  the built assemblies is compared; nothing else of the restored graph is.
* Two builds of the tool from different code can carry the same version string.
* The plan is rewritten during the run, and a write that fails while a stop is in progress is put down
  to the stop, so a disagreement found by that run can survive only in its own stopped report.

### Follow-up actions

* Document the workflow shape for `Reefact/slugger`: a checkout pinned to the series' commit until the
  plan has no remainder, the plan and log carried between nights, and `--stop-after` below the job's
  budget.
* Consider a content identity for a dirty tree, so that a run interrupted on a laptop can continue the
  next morning.
* Record what a `--since` series would be keyed on, in a record of its own.
* Consider carrying the coverage map in the plan, so a resumed run does not pay the coverage pass
  again for a project with a remainder.
