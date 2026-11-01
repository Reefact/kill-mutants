# DEC0026 | A project names the calls it silences, and the report counts what it chose not to hear

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-01 | Proposed | First draft | |
| 2026-11-01 | Accepted | At the maintainer's request | |

## Context

A project can leave three things out of a run: a file or a project, with `--exclude`; a mutator
family, with `--without`, `--with` and `--mutators`; a member, with `[ExcludeFromCodeCoverage]`. All
three work the same way - no mutant is generated, and nothing in the report says that anything was
left out. A mutant that cannot be compiled is the one thing the report counts apart: its status is
`CompileError`, its outcome `Untestable`, and it sits outside the score, which is
`detected / (detected + undetected)`. The JSON report publishes those three counts so that a tool
never re-derives which status counts as what, and the JSON report is the channel the tool offers to
tools ([DEC0023](0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-en.md)).

Whenever the tool recognises something in the code, it does so by the symbol the code binds to,
never by a name. `Statement` leaves a `[Conditional]` call alone by asking the compiler whether the
method carries the attribute
([DEC0020](0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-en.md));
`[ExcludeFromCodeCoverage]` is resolved through the semantic model so that a same-named attribute of
the user's own does not silence a class by accident; the Linq family swaps a call only for a twin
bound on the same type ([DEC0024](0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-en.md)).

`Statement` ([DEC0020](0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-en.md))
was measured alone on `Reefact/slugger` at `c9cf4a2` ([#42](https://github.com/Reefact/kill-mutants/issues/42)):
317 mutants, 148 killed, 144 survived, 25 without coverage, 32 minutes. The survivors sort into
three kinds: 116 argument guards - `ArgumentNullException.ThrowIfNull(x)`,
`ArgumentException.ThrowIfNullOrWhiteSpace(x)`, `ArgumentOutOfRangeException.ThrowIf…(x, …)` -
covered by the tests, which run the line and never pass `null` or a value out of range; 19 calls of
`report.Append(...)` writing report prose nothing asserts on; and 9 others, among them whole report
sections and warnings no test checks are there. The decision taken for that family was to silence
nothing in the tool, as the README's stance on `StringLiteral` already is: report the split rather
than decide for the user. It leaves a project two choices, both blunt: keep the 116 guard survivors
burying the 9 gaps, or `--without Statement` and lose the 9 as well.

On `Reefact/first-class-errors` at `8f84258` ([#44](https://github.com/Reefact/kill-mutants/issues/44)),
three shapes accounted for 66 of the 569 survivors: 42 mutants of `ConfigureAwait(false)` into
`ConfigureAwait(true)`, a `BooleanLiteral` in the call's argument, 7 % of all survivors
([#48](https://github.com/Reefact/kill-mutants/issues/48)); 22 removals of
`context.EnableConcurrentExecution()`, the call statement itself, once per analyzer
([#49](https://github.com/Reefact/kill-mutants/issues/49)); 2 names of `Expression.Parameter` and
`Expression.Variable` emptied, a `StringLiteral` in the call's argument
([#50](https://github.com/Reefact/kill-mutants/issues/50)). Each was filed as an equivalent mutant
the tool should never generate. The review of [#54](https://github.com/Reefact/kill-mutants/pull/54)
established that all three are observable - a test can install a `SynchronizationContext`, supply its
own `AnalysisContext`, or read the expression's public `Name` - and that three kinds of survivor
exist: provably equivalent, which the tool stops generating, as it did for a `catch` filter naming a
type and its subtype; excluded by policy, which a project chooses not to hear; observable and
reported, which stays a survivor. The three shapes are the third kind, and the three issues were
closed in favour of this decision. Two of the three mutate an argument of the call, not the
statement.

[DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md) refused a status meaning "outside
the diff", because Stryker.NET's `ignored` state is a primitive whose meaning is *this does not
count*, and a partial run built on it amputates the denominator while the label, the formula and the
position in the report stay put. Stryker.NET also offers, read in its source and documentation,
`ignore-methods` - calls to methods matching a text pattern are not mutated, with wildcards,
optionally qualified by type and with `.ctor` for constructors, which its documentation notes
ignores the mutations *inside* the call too - and `// Stryker disable|restore [once] [families]`
comments in the source; mutants ignored either way show in its report as `Ignored`.

A series ([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md))
compares `Exclude`, `Mutators`, `Without` and `With` between two runs as sets, and refuses to
continue under a difference. Every option that states a habit has a key in `killmutants.json`; a
list given on the command line replaces the file's; `none` empties a list that the file fills. In
`--exclude`, `*` also matches `/`. The tool has no release: the version is `0.1.0-alpha` and no tag
exists.

The maintainer stated the base against which this was decided: the tool does not lie, it is
pertinent, and it is not a gas factory. The maintainer decided to split the issue in two, this
record carrying the list of calls and a later one the comments in the source, once this one has
been measured; and settled the following points: a recognised call silences its statement and the
expressions that supply its values, not the body of a lambda or local function handed to it;
matching goes through the symbol the call binds to, against the fully qualified name of the method
or constructor, with `*` as in `--exclude`, constructors written as Stryker writes them; a silenced
mutant is generated, not tested, reported as `Ignored` outside the score, and counted per family and
per rule, a rule that silenced nothing being named too; the score line itself carries the count of
ignored mutants; a series refuses to continue under another list; the list has both an option and a
key.

## Decision

In this context, we decide that a project names, with `--ignore-calls` or the `ignoreCalls` key, the
calls whose statement and arguments are not mutated - recognised by the fully qualified name of the
method or constructor the call binds to, with `*` as in `--exclude`, and not reaching the body of a
lambda or local function handed to the call - that each mutant so silenced is generated but not
tested and reported as `Ignored`, outside the score, counted per family and per rule and beside the
score itself, with a rule that silenced nothing named, and that a series refuses to continue under
another list.

## Rationale

The two blunt choices slugger left a project with are what this exists to replace: a project can now
keep `Statement` and hear the 9 gaps without the 116 guards burying them, and the three shapes of
first-class-errors are answered the same way. The project says what it does not want to hear, in a
file it versions with its code, and the tool decides nothing in its place - which is what the review
of #54 settled when it refused to make the tool stop generating mutants a test could observe.

Recognising the call by the symbol it binds to is what the tool does everywhere else, and for the
same reason: a method of the user's own that merely shares a name with `ThrowIfNull` is not the
guard the project meant. Writing the pattern against the fully qualified name, with the `*` the
project already knows from `--exclude`, keeps one dialect of patterns; a bare name recognises
nothing, and is named in the report as a rule without effect, so a wide net has to be written as one.
Constructors are the call behind `throw new ArgumentException("…")`, whose message is the most
ordinary `StringLiteral` noise, and the form Stryker uses for them is the one a project arriving from
Stryker already knows.

Silencing the arguments as well as the statement is what the three shapes of first-class-errors
require, two of them being mutants of an argument. An argument counts only through what the call
makes of it, so a mutant in one is observable only through a call the project has said it does not
hear. The body of a lambda or local function is another matter: it is code the call is handed and
runs later, and its effects escape the call, so silencing it would make the rule say more than the
project wrote.

Reporting a silenced mutant as `Ignored`, counted per family and per rule, is the opposite of what
DEC0010 refused in Stryker's state: there the denominator shrank without the user's choice and
without a trace; here the project chose, the choice is versioned, every count of it is published, and
a rule that no longer recognises anything is named rather than silently obeyed. Putting the count
beside the score itself is what keeps the percentage from lying to a reader who reads nothing else.
Generating the mutant and not testing it is what keeps the choice visible at no cost: generation is
paid per file already, and a test run per silenced guard is exactly what the project asked not to
pay.

A series that mixed two lists could not say what a mutant killed under one and ignored under the
other is; comparing the list as the exclusions are compared, and refusing a difference, is the rule
DEC0015 already applies. A key and an option both exist because every other habit has both, the
command line replacing the file's list: one rule without exception is less to learn than a reason
why this list is different, and the one-evening question - what would the score be without the
guards - is a pertinent one.

## Alternatives considered

### Alternative 1 — The status quo: a family, or a file

* **Description:** Leave the project `--without Statement` and `--exclude`, as today.
* **Why rejected:** Both are blunt. On slugger either keeps 116 guard survivors burying 9 real gaps,
  or loses the 9 with them.

### Alternative 2 — Stop generating those mutants in the tool

* **Description:** What #48, #49 and #50 asked for: a list of APIs the catalogue never mutates.
* **Why rejected:** The three mutants are observable. A tool that stops generating a mutant because
  the usual tests do not catch it decides for every project what it should have tested.

### Alternative 3 — `[ExcludeFromCodeCoverage]` on the method

* **Description:** Mark the methods holding the guards with the attribute the tool already honours.
* **Why rejected:** It silences the whole member, the 9 gaps with the guards, and nothing in the report
  says a member was left out.

### Alternative 4 — Do not generate a silenced mutant at all

* **Description:** Treat a recognised call as `--exclude` treats a file: no mutant, nothing reported.
* **Why rejected:** The README reports `StringLiteral`'s split rather than deciding; a silenced mutant
  nobody can count is a choice the report cannot show, and a stale rule would go on being obeyed
  unseen.

### Alternative 5 — Test the silenced mutants anyway, and report them apart

* **Description:** Generate and test them, and show their verdicts under a heading of their own.
* **Why rejected:** It pays the 116 test runs the project asked not to pay, which on slugger is most of
  a 32-minute run.

### Alternative 6 — Recognise calls by a text pattern on their name

* **Description:** Stryker's `ignore-methods`: a pattern matched against the method's name as written.
* **Why rejected:** A method of the user's own that shares the name is caught by accident, which is the
  reason the tool resolves every attribute and twin through the semantic model.

## Consequences

### Positive

* A project says, in a file it versions, what it chooses not to hear, finer than a file or a family,
  and the real gaps are no longer buried under the guards.
* The choice stays visible: `Ignored` per family and per rule, the count beside the score, and a rule
  that silenced nothing named.
* No test run is paid for a silenced mutant.
* #48, #49 and #50 are answered without the tool deciding for the project.
* Code handed to a call - a lambda, a local function - is never silenced by a rule about the call.

### Negative

* The score rises by the project's own choice; the report says so, and nothing else can.
* A recognised call silences the arithmetic and the literals of its arguments.
* The JSON report gains `ignored` among its totals and a list per rule, which becomes a contract once
  the tool is released; the command line and the file carry one more option and key.
* A series has one more cause of refusal: a list that changed.

### Risks

* A team silences real gaps and reads the report as better than it is.
* The fully qualified name of a symbol, as Roslyn displays it, may change between versions - generic
  types, nested types - and a pattern stop recognising without a word; the rule named as having no
  effect is what catches it.

### Follow-up actions

* Measure on `Reefact/slugger` with the guards listed, and confirm that the 116 become `Ignored` and
  the 9 stay.
* Document the option, the key and the form of a pattern, constructors included, in the `README.md`.
* Decide the comments in the source in a record of their own, once this one is measured.
* Say, when building it, how an `Ignored` mutant sits in the plan and the log of a series.
