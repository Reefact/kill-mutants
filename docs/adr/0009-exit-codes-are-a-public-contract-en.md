# ADR-0009 — Exit codes are a public contract

**Status:** accepted · **Date:** 2026-08-31

## Context

A CI job needs to act on a run's outcome, and the only thing it reliably sees is the exit code.
Scripts will encode whatever we choose, so renumbering later breaks them silently — this is decided
once.

The outcomes worth distinguishing are not obvious. A run can:

- finish, with a score the user is happy with;
- finish, with a score below what they asked for;
- fail to run at all — no project found, a red baseline, a broken environment;
- never start, because the command line made no sense.

Collapsing these into "0 or non-zero" is the tempting simplification. It fails the moment a job wants
to react differently to *your tests got weaker* and *this tool is broken*. A job that cannot tell
them apart will eventually treat a broken environment as a quality regression, or — much worse —
treat a genuinely failing environment as a passing build, because the run never happened and nothing
said so.

## Decision

| code | meaning |
|---|---|
| **0** | Ran, and met the threshold if one was given |
| **1** | Ran, and the gate you asked for did not pass |
| **2** | Could not run; the reason is on standard error |
| **64** | The command line was not understood |

`1` is named for the gate, not for one of its causes, and it already had more than one before
`--since` was thought of. Its cases:

1. the mutation score is below `--break-at`;
2. the score is **undefined** because nothing could be tested, which cannot be shown to meet a
   threshold - shipped behaviour, in `Program.Verdict`, since before
   [ADR-0010](0010-a-partial-run-reports-findings-not-a-score-en.md);
3. a partial run found what the caller asked it to fail on, or could establish nothing at all.

All three are *what you asked me to check did not pass*, which is what a build script branches on;
standard error says which. That is why the constant is `GateNotPassed` rather than
`ScoreBelowThreshold` - the old name was already wrong for case 2.

`--break-at` is **opt-in**. With no threshold, a low score is reported and the run still exits 0.
A default threshold would make adopting KillMutants a breaking change for every build that added it.

An **undefined score fails a threshold**. If nothing could be tested, the run has demonstrated
nothing, and reporting success would let a misconfigured job stay green forever.

`64` follows the long-standing `EX_USAGE` convention, which keeps it clear of any code the run itself
might mean.

## We diverge from Stryker.NET here, deliberately

Stryker uses `1` for a general error and `2` for a threshold violation. We use the opposite mapping,
which is the one linters and formatters use: `1` means *the thing you asked me to check is not good
enough*, `2` means *I could not check it*.

Both are defensible. Ours matches how the tool is actually invoked — as a quality gate alongside
other checkers, where a job's `|| exit 1` habit should mean "findings", not "crash". Anyone scripting
both tools will need to read this table, so it is stated rather than left to be discovered.

## Consequences

- A CI job can be precise: fail the build on `1`, page someone on `2`.
- The mapping is now testable, and is tested by running the real executable and asserting on the
  code it returns, rather than by asserting on the policy behind it. Testing anything else would
  test our intention instead of the contract.
- A code is never renumbered, and a new *kind* of outcome gets a new code. A new *cause* of an
  outcome a code already names joins it instead — which is what
  [ADR-0010](0010-a-partial-run-reports-findings-not-a-score-en.md) did to `1`, whose two causes are
  now a score below a threshold and a newly undetected mutant in a partial run. The distinction is
  what a build script can act on: it branches on "findings" versus "could not check", not on which
  finding.
