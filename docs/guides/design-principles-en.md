# Design principles

🌍 **Languages:**  
🇬🇧 English (this file) | 🇫🇷 [Français](./design-principles-fr.md)

Every tool refuses something. Most do it by accident and apologise in the issue tracker.
KillMutants does it on purpose and writes the boundary down. This page explains where that boundary
runs, so you can decide whether it is the right tool for you — and so its refusals stop looking
like gaps.

Each principle links to the decision record that argues it in full. A record is the repository's
memory of *why*; this page is what those decisions mean for you.

## Narrow on purpose

**Supported:** xUnit 4, modern .NET, C#, SDK-style projects.

**Not supported, and no abstraction exists in anticipation of them:** xUnit 2 and earlier, NUnit,
MSTest, TUnit, VSTest, .NET Framework, non-SDK project formats, `packages.config`, F#, Visual Basic.

The reference tool for .NET carries ten years of compatibility: a fifth of its files are options,
thousands of lines serve older test platforms and older project formats. The part that actually
performs mutation testing is a few files long. KillMutants keeps that part and declines the rest,
and the constraints are what let the design stay small enough to be trusted
([architecture](../architecture-en.md)).

KillMutants takes no dependency on any xUnit package. It launches the test project's executable as
a child process; the coupling is a knowledge of a command-line contract, not a reference.

## A number you cannot explain is not a measurement

Several choices follow from one rule: **every number in a report is one a reader can check**.

* The timeout budget is printed as the formula it is — `0.4 s baseline alone × 3 + 30 s` — because a
  tool that times out half its mutants under a budget it never wrote down has produced a headline,
  not a finding.
* Every kill names the tests that failed, so a doubted verdict can be reproduced by hand.
* Every mutant carries a key derived from what it is, so two reports of one commit can be joined.
* `--verify-kills` re-runs a sample of the kills alone and reports any that do not repeat, because
  a flaky test produces a kill the mutation did not cause.
* The `Ran on` line records the runtime, the machine and the xUnit version, because two reports
  without it are not comparable and nobody notices.

## The score is honest, or it is nothing

A mutant nothing covers counts as **undetected**. The suite would not have noticed the change;
skipping its test run is an optimisation, not a pardon. Excluding it would mean a project could raise
its score by adding code no test touches.

And where no honest percentage exists, none is printed:

* **A partial run prints no score**
  ([DEC0010](../decisions/0010-a-partial-run-reports-findings-not-a-score-en.md)). Its population
  is the diff, chosen against a base that differs every run, so a percentage over it answers nothing
  that spans two runs. It prints findings and a verdict instead.
* **A stopped run prints no score and passes no gate**
  ([DEC0013](../decisions/0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-en.md)). The
  mutants it judged are the ones it reached first, not a sample.
* **An undefined score fails the gate.** A run that could test nothing cannot be shown to have met a
  threshold, and reporting success would let a misconfigured job stay green for ever.
* **A partial run withholds its verdict** when it could not read the earlier state in full
  ([DEC0012](../decisions/0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-en.md)),
  rather than refusing to run or passing with a warning attached.

## Verdicts are isolated, whatever it costs

Each mutant gets its own compilation
([DEC0002](../decisions/0002-one-compilation-per-mutant-en.md)) and its own fresh test process in a
private copy of the test output
([DEC0008](../decisions/0008-never-reuse-a-test-host-between-mutants-en.md)). Reusing a warm host
across mutants is the most tempting optimisation available and the one the tool refuses: it is the
source of the longest-standing correctness complaint against tools that do it, where process-global
state leaks between mutants and inflates scores.

The cost is a directory copy and a process launch per mutant. What it buys is the guarantee that no
two mutants can ever observe each other — and that KillMutants never writes into your build output
at all.

## The baseline is verified before anything is mutated

The unmutated code is emitted, injected and tested through exactly the path a mutant takes, and the
suite has to be green. A compilation that differs from the real build in any way — a missing
generated file, a wrong preprocessor symbol — makes the tests fail for reasons unrelated to the
mutation, and every mutant is then reported killed. A tool that always says *killed* is worse than
no tool, because it is silently reassuring. A project whose baseline is red is set aside with its
reason, and the rest of the run carries on
([DEC0022](../decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-en.md)).

## A run carries on past what fails, and says so

A project MSBuild cannot read, a test project that does not build, a red baseline, a failed coverage
pass: each used to stop the run. Now each is **set aside** with its reason, named at the top of the
report, and the rest is measured as usual. Such a run passes no gate and exits `2` — not `1`,
because what went wrong is in the environment or the suite, not in the tests, and a job that cannot
tell the two apart will eventually treat a broken environment as a quality regression.

Exit codes are a public contract: a code is never renumbered, and a new cause of an outcome a code
already names joins it ([DEC0009](../decisions/0009-exit-codes-are-a-public-contract-en.md)).

## The catalogue is selective, and the choice is yours

Every mutant costs a test run, so an operator earns its place by the signal it carries. `+x` is
never removed, because the mutant is equivalent by construction; `?.` and `as` are left alone,
because their mutants measure whether a test touches the line rather than whether it checks the
result. [The mutators](./mutators-en.md#what-is-deliberately-not-mutated) lists every omission
with its reason.

Within the catalogue, the tool measures and reports rather than deciding for you. `StringLiteral`
produces many mutants and detects few on most projects; those are true findings, valuable where
messages are asserted on, so the report shows the split per family and `--without` acts on it.
`--ignore-calls` goes finer, and what it silences is counted beside the score it moved
([DEC0026](../decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-en.md)).
Nothing is hidden from the reader; the project chooses what to hear, in a file versioned with its
code.

## Nothing about the terminal is detected

What standard error shows is chosen with `--progress`, and `log` — dated lines that read anywhere —
is the default
([DEC0023](../decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-en.md)).
A tool that guesses from the stream gets it wrong in exactly the places that matter: a CI log full
of carriage returns, a terminal that shows nothing because the output went through a pipe. An
explicit choice is one less thing to be surprised by.

## Where to go next

| If you want to… | Read |
| --- | --- |
| read the decisions themselves | [Decision records](../decisions/README.md) |
| see the measurements the principles rest on | [Architecture](../architecture-en.md) |
| see what the catalogue leaves alone, and why | [The mutators](./mutators-en.md#what-is-deliberately-not-mutated) |

---

[← Documentation index](../README.md)
