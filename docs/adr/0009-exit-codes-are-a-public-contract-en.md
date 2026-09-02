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
| **1** | Ran, and found what you asked it to fail on |
| **2** | Could not run; the reason is on standard error |
| **64** | The command line was not understood |

`1` began life as *the score is below `--break-at`* and was broadened by
[ADR-0010](0010-a-partial-run-reports-findings-not-a-score-en.md), which adds a partial run that has
no score and fails on a newly undetected mutant instead. The row above is the general form its
reasoning always implied; the two cases are the score below a threshold, and a new undetected mutant
in a partial run. A build script reading `1` learns "findings", which is what it acts on.

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
- Adding an outcome later means adding a code, never renumbering an existing one.
