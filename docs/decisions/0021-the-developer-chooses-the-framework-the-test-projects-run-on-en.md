# DEC0021 | The developer chooses the framework the test projects run on, and an ambiguous run is refused without that choice

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-31 | Proposed | First draft | |
| 2026-10-31 | Accepted | At the maintainer's request | |

## Context

KillMutants refuses a test project that targets several frameworks, with a message saying that each
would need its own run and its own score, and it refuses a project reached by test suites on
different frameworks for the same reason. A test project built for `net8.0;net10.0` is ordinary C#,
and today it has no way to be run at all.

The framework a library is analysed for was, until the change this record accompanies, the test
project's own, which stopped any run over a library built for another framework. It is now the one
NuGet negotiates from the test project's restore result (RB-007), so a library follows the test
project that loads it. Nothing yet lets the developer say which framework a test project runs on.

The maintainer asked, on 2026-10-31, whether the developer should be able to target a particular
framework, and whether other options were wanted. The study of Stryker.NET in this repository lists
multi-TFM disambiguation among the things that tool carries only for history.

Exit codes are a public contract (DEC0009): `64` is for a command line that was not understood and `2`
for a run that could not be completed. Every option that states a habit has a key in
`killmutants.json`. A run's plan records its configuration, coverage, mutators, exclusions, scope and
fingerprint (DEC0016), and a resumed run inherits the verdicts its series reached on the same code
(DEC0015); the plan records no framework.

A mutation score describes a single build of the code: each framework produces its own assembly, its
own output and its own verdicts (RB-007). The gate (`--break-at`) judges a run's one score.

## Decision

In this context, we let the developer choose, with one `--target-framework` option and a
`targetFramework` key of `killmutants.json`, the single framework the test projects run on, and we
refuse, naming that option, a run whose frameworks are ambiguous without it.

## Rationale

The ordinary case that is refused today, a test project on several frameworks, has no way out except
this choice, and the refusal can only be lifted by the person who knows which framework matters.

The option selects the framework of the test projects and not of the libraries because the libraries
already follow from the test project that loads them (RB-007): a second control over the same fact
would be a second way to disagree with the first. Choosing the suites' framework is also what
unblocks the case that is refused.

An ambiguous run stays refused without the option, rather than picking a default, because a score
describes one build: a default would produce a score for a framework nobody chose, which is the
reason the refusal exists. The message now names the option and the values on offer, so the refusal
becomes a way forward.

One framework per run keeps one score per report, so the gate still has a single score to judge and a
resumed series a single build to continue.

A value that no project offers is a well-formed command line, and the project files are what rule it
out, which is only known after discovery; `64` stays reserved for the command line's syntax
(DEC0009), so the run ends with `2`. For the same reason of never losing code silently, a test
project that does not offer the chosen framework stops the run and is named: skipping it would leave
the code that only it tested out of the report without a word, and `--exclude` already exists for
setting a project aside on purpose.

A `killmutants.json` key follows from habits having keys. The framework has to enter the run's plan,
or a resumed series could inherit verdicts reached on a different build.

## Alternatives considered

### Alternative 1 — Keep refusing, with no way to choose

* **Description:** the status quo: a test project on several frameworks stays unrunnable.
* **Why rejected:** an ordinary project cannot be mutated at all, and the refusal gives its author
  nothing to do about it.

### Alternative 2 — Force the library's framework directly

* **Description:** the option selects the build analysed for each library and bypasses the
  negotiation.
* **Why rejected:** it does not unblock a test project on several frameworks, which is the refused
  case, and it duplicates a fact the negotiation already settles.

### Alternative 3 — Two options, one for the test projects and one for the libraries

* **Description:** both controls are exposed.
* **Why rejected:** more public surface to keep for good, and a second control that can contradict the
  first, for a case the negotiation already covers.

### Alternative 4 — Pick a default when the choice is ambiguous

* **Description:** take the first framework offered when the developer says nothing.
* **Why rejected:** the score would describe a framework nobody chose.

### Alternative 5 — Accept a list of frameworks and report each

* **Description:** `--target-framework net8.0,net10.0` runs once per framework and writes a report for
  each.
* **Why rejected:** it asks for a rework of what identifies a mutant and a report, and of the gate,
  for a convenience the developer already has by running twice.

### Alternative 6 — Skip the test projects that do not offer the framework

* **Description:** leave them aside with a warning.
* **Why rejected:** the code that only they tested would drop out of the report without the score
  showing it.

### Alternative 7 — Exit with `64` for a value no project offers

* **Description:** treat the unavailable value as a usage error.
* **Why rejected:** it widens `64` beyond the command line's syntax, which DEC0009 reserves it for.

## Consequences

### Positive

* A test project on several frameworks can be run, and the refusal that remains says what to type.
* A score and a report keep describing one framework.
* Libraries keep following the test project that loads them, with no second control to disagree.

### Negative

* A public option and a configuration key to keep for good.
* Covering several frameworks means one run per framework.
* A solution mixing test projects on different frameworks needs `--exclude` for the ones that do not
  offer the chosen one.

### Risks

* A script that pins a framework fails with `2` the day a project retargets.
* A developer used to a default may read the refusal as a regression.

### Follow-up actions

* Put the chosen framework in the run's identity, which a resumed run is compared against, so that it
  never inherits verdicts reached on another framework (DEC0015, DEC0016).
* Document the option and the key in the README and in `--help`.
* Update RB-007 and give the two refusals a message that names the option and the values on offer.
