# FAQ

🌍 **Languages:**  
🇬🇧 English (this file) | 🇫🇷 [Français](./faq-fr.md)

Short answers to the questions that come up most. Each links to the page that covers the topic
properly.

## Choosing the tool

### How is this different from Stryker.NET?

Stryker.NET is the reference implementation for .NET, and it was studied closely before a line of
KillMutants was written ([the study](../study/stryker-net-en.md)). The differences are choices,
not gaps:

* **Scope.** xUnit 4 and modern .NET only; no VSTest, no .NET Framework, no older project format.
* **One compilation per mutant**, rather than one instrumented build with every mutant switched at
  run time — and a fresh test process per mutant, never a reused host.
* **An honest score**: uncovered mutants count as undetected, and a partial or stopped run prints
  no score at all.
* **A pull-request mode** that answers with a verdict in seconds, and widens to what a changed
  test exercised.

[Design principles](./design-principles-en.md) argues each one.

### Does it support NUnit, MSTest or TUnit? .NET Framework?

No, and no abstraction exists in anticipation of them. The coupling to xUnit 4 is a knowledge of
its runner's command line, which is what keeps the tool small. See
[Design principles](./design-principles-en.md#narrow-on-purpose).

### Does it need anything in my test projects?

No package, no attribute, no base class. A test project is recognised by its reference to the
`xunit.v3` package family at version 4 — `xunit.v3`, `xunit.v3.core`, `xunit.v3.mtp-v2` or
`xunit.v3.core.mtp-v2`.

## Reading the results

### My code is 100 % covered. Why is the score 46 %?

Because coverage says a line was *executed* and a mutation score says it was *checked*. A test can
run `orderTotal >= 100m` a thousand times without ever trying 100. See
[Getting started](./getting-started-en.md#what-is-a-mutant).

### Why do uncovered mutants lower the score?

Because the suite would not have noticed them either. Skipping their test run is an optimisation,
not a pardon, and excluding them would let a project raise its score by adding code no test touches.
See [Core concepts](./core-concepts-en.md#the-score-and-why-uncovered-code-counts).

### Why is there no score on a `--since` run?

The population is the diff, chosen against a base that differs every run, so a percentage over it
cannot be compared with any other run. A partial run prints findings and a verdict instead. See
[Judging a pull request](./pull-requests-en.md#what-it-prints).

### A mutant survived and I cannot write a test that kills it. What now?

It may be **equivalent**: a change that does not alter the program's behaviour, which no test can
detect. The mutators avoid producing those wherever it can be known statically, but the general
case is undecidable. Read it, and if it is equivalent, leave it — it is a known cost of the method,
not a defect in your tests. If it is a guard clause the tests legitimately never trigger,
[`--ignore-calls`](./mutators-en.md#silencing-calls-you-do-not-want-to-hear-about) silences that
call and counts what it silenced.

### A mutant was killed and I do not believe it.

Every kill names the tests that failed, under `killedBy` in the JSON report. Put the mutation in the
file at the position given, run those tests, watch them fail. If you suspect a flaky test,
`--verify-kills` re-runs a sample of the kills alone and reports any that do not repeat. See
[Reading a report](./reading-a-report-en.md#re-verifying-kills).

### Why does the score change when I leave out a family?

Because a different question was asked. A score is only comparable with another score from the
same catalogue, so pick one for a CI job and keep it in `killmutants.json`. See
[The mutators](./mutators-en.md#choosing-a-catalogue).

### What is the timeout budget on the last line?

How long a mutant's tests may run before it is declared a timeout: the unmutated suite's duration,
times three, plus thirty seconds. It is printed as a formula so a reader can check it. See
[Core concepts](./core-concepts-en.md#the-timeout-budget-is-written-down).

## Running it

### How long does a run take?

Measured on this repository: 384 mutants over the core assembly, 6.8 minutes on four cores, with
three quarters of the time spent testing mutants and a fifth measuring coverage. A `--since` run
over a pull request takes seconds. The [architecture](../architecture-en.md) has the breakdown.

### My CI job has a time limit. What happens when the run hits it?

Give the run a deadline of its own with `--stop-after`, below the job's limit. It then reports the
verdicts it reached, prints no score, passes no gate, and leaves a log and a plan beside the report
for whatever it did not reach. `--resume` finishes the rest in a later job. See
[Continuous integration](./continuous-integration-en.md#a-run-that-outgrows-its-budget).

### The run says a project was "set aside". Is my score wrong?

The score covers the part that was measured, and the report says so. A project MSBuild could not
read, a test project that did not build, or a suite that fails against unmutated code is named at
the top with its reason; fix that, and the next run measures it. The run exits `2`, which tells
a job the environment is at fault rather than the tests. See
[Continuous integration](./continuous-integration-en.md#when-part-of-the-run-cannot-be-measured).

### The run says my test project targets several frameworks and stops.

A score describes one build of the code, so pass `--target-framework net10.0` (or
`targetFramework` in `killmutants.json`). Run it again for another framework. See
[Continuous integration](./continuous-integration-en.md#pointing-it-at-the-right-code).

### Can I run it on my builders and test helpers?

You can, but you probably do not want the findings. A library of builders, fakes and assertion
helpers declares `<KillMutantsTestSupport>true</KillMutantsTestSupport>` and is left out without
hiding the code behind it. See [The mutators](./mutators-en.md#leaving-code-alone).

### Why does it refuse `*.received.*` in `--ignore-untracked`?

Because that pattern also matches `x.received.cs`, a file that changes what is built. Name the
extensions instead: `*.received.html`, `*.received.json`. See
[Continuous integration](./continuous-integration-en.md#files-the-tests-themselves-write).

### Where do the mutated assemblies go?

Into a private copy of the test project's output, one per worker, never into your `bin`. Nothing
of the tool is written into your build output.

---

[← Documentation index](../README.md)
