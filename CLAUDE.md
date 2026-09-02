# KillMutants

A mutation testing tool for .NET 10. The solution is `KillMutants.slnx`; the entry point is
`src/KillMutants.Cli`.

This file holds the conventions **no tool in this repository enforces** — which is exactly why they
are written down: nothing will stop you breaking them. What the build refuses, it refuses loudly and
by itself, and is not restated here.

## A decision that is hard to reverse gets recorded

The test is DEC0001's, and it is narrow: a choice earns a record only when it is **hard to reverse**
*and* admitted **more than one reasonable answer**. A choice that follows from the project's stated
constraints is not recorded, however large it feels.

When a change meets that test, stop and use the `decision-record` skill
(`.claude/skills/decision-record/SKILL.md`), which carries the method. The repository's own
conventions — where records live, how they are named and numbered, who accepts them — are in
`docs/decisions/README.md`. Neither is repeated here: two copies of a rule drift, and then the
reader cannot tell which one is current.

Four boundaries *are* repeated, because they are irreversible and you may not have opened either
file:

- You **draft and propose**. You never accept, reject, deprecate or supersede a record.
- You never add a status row on your own authority.
- You never rewrite an accepted record. A decision that evolves becomes a *new* record, and the old
  one's status gains a row.
- You never renumber a `DECnnnn` or an `RB-nnn`. Both are cited from source comments and from the
  documentation, so a number is a permanent handle.

## Do not write what the code does not do

`README.md` published one cause of exit code `1` while `Program.Verdict` returned it for two. That
is a defect in a public contract, and it survived because the prose and the code were edited
separately.

So: when the documentation and the code disagree, establish which one is wrong before editing
either. And a decision that has been recorded but not built — `--since`, decided in DEC0010 and
DEC0011, absent from `KillMutants.Cli` — is not documented as though it existed. A record says what
was decided; the README says what the tool does today.

## The reasoning goes where it applies

Comments here argue **why** a choice was made and what it rules out, not what the code does.
`src/KillMutants.Cli/ExitCode.cs` and the header of `.github/workflows/ci-preview.yml` are the
readable examples. A change that keeps the mechanism and drops the reason throws away the expensive
half.

## The documentation is bilingual

`docs/architecture`, `docs/robustness-backlog`, `docs/study/stryker-net` and every decision record
exist as `-en.md` and `-fr.md`. **The English is canonical**; the French changes with it, in the
same pass. Nothing checks this, so a half-translated change goes green. The root `README.md` is
English only.

## Commits and history

- A conventional prefix (`fix`, `feat`, `docs`, `ci`, `chore`, `test`, `perf`), then **a sentence,
  not a label**: `fix: the compiler server outlives the directory it loaded an analyzer from`.
  Subjects run long here — up to 96 characters — because a sentence that says what changed beats a
  short one that does not.
- The body argues why, says what was checked before deciding, and cites the `RB-nnn` or `DECnnnn` it
  touches, wrapped between 72 and 80 columns as the history is.
- `main` is **strictly linear**: `git log --merges origin/main` is empty. Rebase; never `git merge`
  into a pull request branch. A merge commit blocks GitHub's rebase-merge, and the branch then has
  to be rebuilt.
- Nothing enforces any of this. There is no commit-msg hook and no lint.

## A gate is not a signal

Neither `ci-preview` nor `selfcheck` gates, and they do not mean the same thing by it.

`ci-preview` is allowed to fail without failing its workflow — `continue-on-error: true` — because
*"a red gate on somebody else's preview churn teaches people to ignore red gates"*.

`selfcheck` carries no `continue-on-error` at all. What it declines is a **score threshold**, since
a bar drawn near the current score would measure nothing but itself; a run that could not complete
is a real signal and is left to fail the workflow.

The distinction matters the moment you edit either. Adding `continue-on-error` to `selfcheck` would
look like preserving the convention and would in fact throw away the only failure it still reports.
Turning a signal into a gate, or a gate into a signal, is a decision rather than a tidy-up.

And never make a gate green by weakening what it checks. A skipped, disabled or quarantined test is
not a fix.

## What you never do

- Merge a pull request, or push to `main`.
- Accept a decision record, or add a status row to one.
- Renumber a `DECnnnn` or an `RB-nnn`.
- Tag a release. `RELEASING.md` holds the procedure and its recovery, which turns on whether NuGet
  already has the version: a run that failed **before** publishing leaves a tag describing a release
  that did not happen, so the tag is deleted; a run that failed **after** the push keeps its tag,
  the packages being out and immutable, and you go forward to the next version. A version
  half-published is burned either way.

## Where things live

| Path | What it holds |
|---|---|
| `src/KillMutants.Cli` | the command line, the exit codes, the verdict |
| `src/KillMutants.Core` | the engine |
| `tests/` | the suite, including end-to-end tests that run the real executable |
| `docs/decisions/` | `DECnnnn` — why a choice was made, and what it ruled out |
| `docs/robustness-backlog-*` | `RB-nnn` — edge cases carried as specifications |
| `docs/architecture-*` | the pipeline, the domain model, the risks |
| `RELEASING.md` | trains, tags, and what to do when a release fails |
| `.github/workflows/` | the gate, the advisory signals, and the reasoning for both |

## What the build already enforces

`Directory.Build.props` sets `Nullable`, `TreatWarningsAsErrors`, `EnableNETAnalyzers` at
`latest-recommended` and `EnforceCodeStyleInBuild`; `.editorconfig` raises file-scoped namespaces,
required braces and accessibility modifiers to warnings, which the first of those turns into errors.
None of it is restated here — the build refuses it and names the line. `.github/workflows/ci.yml`
holds the authoritative build and test invocation.
