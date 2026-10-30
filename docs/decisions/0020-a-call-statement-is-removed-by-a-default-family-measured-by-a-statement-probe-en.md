# DEC0020 | A call statement is removed by a default family, and its coverage is measured by a probe placed before it

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-10-30 | Proposed | First draft | |
| 2026-10-30 | Accepted | | |

## Context

Issue #21, filed from the comparison of the catalogue with Stryker.NET 5.0.0 on `Reefact/slugger`
(#15), measured Stryker's `Statement mutation` there at 147 mutants created and 139 judged, against
199 created and 29 judged for its `Block removal mutation`. It asked whether statement removal
belongs in the catalogue, whose inventory refused `switch` arms as "a structural mutation, not an
operator one", needing "a different kind of reasoning about exhaustiveness".

Read at Stryker.NET's commit `b826642`, its `StatementMutator` is enabled at the default level and
replaces a statement with `;`. It removes expression statements, a `return` without a value,
`break`, `continue`, `goto`, `throw` and `yield`. It refuses an expression statement that holds an
assignment or an `out` argument, a `return` with a value, and control flow inside a `switch`
section, and leaves the rest to the compilation and rollback of its mutant schemata.

Every family in the catalogue mutates a value or a decision. Removing
`Directory.CreateDirectory(path);` or `Config.Save(merged);` changes neither, and #21 reports that
on slugger two of its three examples went unchecked by the tests until tests were written for them.
Removing an assignment to a local can leave it unassigned where it is read (CS0165). Removing a
`return`, a `throw` or a `break` changes what the compiler concludes about reachability, as the
probe's own history with constant conditions showed (RB-029). An empty statement as the body of an
`if` draws CS0642; an empty block does not. A call to a method marked `[Conditional]`, such as
`Debug.Assert`, is dropped by the compiler when its symbol is undefined, which is the case in the
Release build a run makes by default.

DEC0007 measures coverage with `Hit<T>(id, value)`, which wraps an expression and returns its value.
A statement has no value, and a call to a `void` method cannot be a type argument, so `Hit` cannot
record either. A site without a recorder has its mutants tested against the whole suite, which on
slugger takes about 27 seconds a run.

Families #16 to #19 entered the default catalogue; `NumericLiteral` is opt-in (DEC0019).

On 2026-10-30 the maintainer arbitrated four points: statement removal enters the catalogue; the
first version removes call statements only; its coverage is measured by a statement-level probe; the
family belongs to the default catalogue.

## Decision

A default `Statement` family replaces each statement that only calls something with an empty block,
and the coverage pass records such a statement by a call placed before it, in a block of their own.

## Rationale

The boundary drawn at `switch` arms was drawn against the reasoning they demand, exhaustiveness,
not against structure as such. Removing a call needs none of it: whether the result compiles is the
only question, and the catalogue already asks it of every family. So statement removal fits the
catalogue's own rule, and it asks what no other family can - whether an effect happened at all.

Calls only, because a call is the one statement whose removal compiles without flow analysis.
Assignments and control flow fail on definite assignment and reachability, which is exactly what
makes Stryker's block removal lose most of its mutants; leaving them out keeps every mutant
compilable by construction, as #16 to #19 were. That is also where Stryker's judged statement
mutants are, since it refuses assignments too. A call writing an `out` argument is an assignment in
that sense and is refused with them; a `[Conditional]` call is refused because its removal is
invisible in the build a run makes, and its mutant could never be killed.

An empty block, because it stands wherever the call stood without a warning of its own.

A probe before the statement, because the alternative is the whole suite for every such mutant, and
the family is meant to run by default. A call placed before a statement in a block that declares
nothing changes neither what the statement does nor any scope, which keeps DEC0007's property - the
instrumentation cannot change the program's behaviour - for a site that has no value to return.

Default, as #16 to #19 were, because the family carries high signal and its mutants compile;
opt-in is for a family whose noise would cost every project that did not ask for it, which DEC0019
reserved for literals.

## Alternatives considered

### Alternative 1 — Keep the boundary and refuse statement removal

* **Description:** record that structural mutations stay out, citing #21's measurement.
* **Why rejected:** the boundary's reason is exhaustiveness reasoning, which a removed call does not
  need, and the question it asks is asked by no other family.

### Alternative 2 — Stryker's scope: control flow as well as calls

* **Description:** also remove `return;`, `break`, `continue` and `throw`.
* **Why rejected:** they change reachability and definite assignment, so some mutants would not
  compile without a flow analysis the family does not make.

### Alternative 3 — Calls and assignments to fields

* **Description:** also remove `this.name = name;`, which compiles where an assignment to a local
  may not.
* **Why rejected:** it needs telling fields from locals in every assignment, for a first version
  whose point is to stay compile-safe by construction.

### Alternative 4 — No probe, the whole suite

* **Description:** leave statement sites without a recorder.
* **Why rejected:** about 139 × 27 s more on slugger, for a family that runs by default.

### Alternative 5 — Opt-in, through `--with`

* **Description:** leave the default catalogue and its scores untouched.
* **Why rejected:** most users would never see a family whose signal is high and whose mutants
  compile.

## Consequences

### Positive

* A call whose effect nothing checks is reported, which no family could do before.
* Every mutant the family proposes compiles, and each runs only against the tests that reach it.

### Negative

* Every project's default catalogue grows, so its score is not comparable to one taken before.
* Calls whose effect is logging or diagnostics are removed too, and their survivors are true
  findings of little value, as `StringLiteral`'s error prose is.
* Assignments and control flow stay unmutated.

### Risks

* A call whose removal makes no observable difference - an idempotent refresh, a cache warm-up -
  yields a mutant that cannot be killed.

### Follow-up actions

* Update the catalogue table and the inventory of what is not mutated in `architecture-en.md` and
  `architecture-fr.md`, and the `README.md`.
* Measure the family on slugger.
* Revisit assignments to fields once that measurement is in.
