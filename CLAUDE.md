# KillMutants

A mutation testing tool for .NET 10. The solution is `KillMutants.slnx`, the entry point is
`src/KillMutants.Cli`, and `.github/workflows/ci.yml` holds the authoritative build and test
invocation.

This file holds the conventions **no tool in this repository enforces** — which is exactly why they
are written down: nothing will stop you breaking them. What the build, a workflow or a script
already refuses is not restated here. Those refuse loudly, by themselves, at the point it happens.

## A decision that is hard to reverse gets recorded

The test is DEC0001's, and it is narrow: a choice earns a record only when it is **hard to reverse**
*and* admitted **more than one reasonable answer**. A choice that follows from the project's stated
constraints is not recorded, however large it feels.

When a change meets that test, stop and use the `decision-record` skill
(`.claude/skills/decision-record/SKILL.md`), which carries the method. Where records live, how they
are named and numbered, and who accepts them are in `docs/decisions/README.md`.

## Do not write what the code does not do

When the documentation and the code disagree, establish which one is wrong before editing either.
A decision that has been recorded but not built is not documented as though it existed: a record
says what was decided, the README says what the tool does today.

## The reasoning goes where it applies

Comments here argue **why** a choice was made and what it rules out, not what the code does.
`src/KillMutants.Cli/ExitCode.cs` and the headers of `.github/workflows/ci-preview.yml` and
`selfcheck.yml` are the readable examples — the last two carry the whole argument for why neither of
them gates, at the point where someone would be tempted to make them. A change that keeps the
mechanism and drops the reason throws away the expensive half.

## The documentation is bilingual

`docs/architecture`, `docs/robustness-backlog`, `docs/study/stryker-net` and every decision record
exist as `-en.md` and `-fr.md`. **The English is canonical**; the French changes with it, in the
same pass. The root `README.md` is English only.

`ci.yml` refuses the mechanical half of that. The half that stays here is the half no tool reaches:
nothing establishes that the French *says* the same thing as the English. A twin rewritten to mean
something else passes every check, and only a reader catches it.

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

## What you never do

- Merge a pull request, or push to `main`.
- Accept, reject, deprecate or supersede a decision record, or add a status row to one. You draft
  and propose; the maintainer accepts.
- Rewrite an accepted decision record. A decision that evolves becomes a *new* record, and the old
  one's status gains a row.
- Renumber a `DECnnnn` or an `RB-nnn`. Both are cited from source comments and from the
  documentation, so a number is a permanent handle.
- Tag a release. `RELEASING.md` holds the procedure and its recovery.
- Make a gate green by weakening what it checks. A skipped, disabled or quarantined test is not a
  fix.
