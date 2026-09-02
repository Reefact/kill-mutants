# DEC0009 | Exit codes are a public contract

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

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
to react differently to *your tests got weaker* and *this tool is broken*. A job that cannot tell them
apart will eventually treat a broken environment as a quality regression, or — much worse — treat a
genuinely failing environment as a passing build, because the run never happened and nothing said so.

"The gate did not pass" already had more than one cause before `--since` was thought of. A score below
`--break-at` is one; a score that is **undefined** because nothing could be tested is another, and
`Program.Verdict` has failed a threshold on it since before [DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md).

Stryker.NET uses `1` for a general error and `2` for a threshold violation. Linters and formatters use
the opposite mapping: `1` means *the thing you asked me to check is not good enough*, `2` means *I
could not check it*. `64` is the long-standing `EX_USAGE` convention.

## Decision

In this context, we fix KillMutants' exit codes as a public contract — `0` for a run that met any
threshold given, `1` for a run whose gate did not pass, `2` for a run that could not happen, and `64`
for a command line that was not understood — with `--break-at` opt-in and an undefined score never
counting as a threshold met.

## Rationale

Four codes rather than two, because a job that cannot tell *your tests got weaker* from *this tool is
broken* will eventually act on the wrong one — and the dangerous direction is the silent one, where a
run that never happened is read as a pass.

`1` is named for the gate rather than for one of its causes. It covers a score below `--break-at`, a
score undefined because nothing could be tested, and a partial run that found what the caller asked it
to fail on or could establish nothing at all. All three say *what you asked me to check did not pass*,
which is what a build script branches on; standard error says which. That is why the constant is
`GateNotPassed` rather than `ScoreBelowThreshold` — the old name was already wrong for the undefined
score.

`--break-at` is opt-in because a default threshold would make adopting KillMutants a breaking change
for every build that added it.

An undefined score fails a threshold because, if nothing could be tested, the run has demonstrated
nothing, and reporting success would let a misconfigured job stay green forever.

`64` follows `EX_USAGE`, which keeps it clear of any code the run itself might mean.

The mapping is the linter's rather than Stryker.NET's because it matches how the tool is actually
invoked — as a quality gate alongside other checkers, where a job's `|| exit 1` habit should mean
"findings", not "crash".

## Alternatives considered

### Alternative 1 — Collapse the outcomes into "0 or non-zero"

* **Description:** report success or failure and let the operator read the logs for the reason.
* **Why rejected:** it fails the moment a job wants to react differently to a weakened test suite and
  a broken tool, and its worst case is silent: a genuinely failing environment read as a passing
  build, because the run never happened and nothing said so.

### Alternative 2 — Follow Stryker.NET's mapping

* **Description:** `1` for a general error and `2` for a threshold violation, matching the other .NET
  mutation testing tool.
* **Why rejected:** both mappings are defensible, but ours matches how the tool is invoked — as a
  quality gate alongside linters and formatters, whose convention it shares.

### Alternative 3 — Ship a default threshold

* **Description:** apply a `--break-at` value out of the box so that a low score fails a build without
  configuration.
* **Why rejected:** it would make adopting KillMutants a breaking change for every build that added
  it.

## Consequences

### Positive

* A CI job can be precise: fail the build on `1`, page someone on `2`.
* The mapping is testable, and is tested by running the real executable and asserting on the code it
  returns, rather than by asserting on the policy behind it. Testing anything else would test our
  intention instead of the contract.

### Negative

* We diverge from Stryker.NET, so anyone scripting both tools has to account for the different
  mapping. The public mapping is documented in `README.md` rather than left to be discovered.

### Risks

*Not recorded at the time of the decision.*

### Follow-up actions

* Never renumber a code. A new *kind* of outcome gets a new code; a new *cause* of an outcome a code
  already names joins it instead — which is what
  [DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md) did to `1`. The distinction is what
  a build script can act on: it branches on "findings" versus "could not check", not on which finding.
