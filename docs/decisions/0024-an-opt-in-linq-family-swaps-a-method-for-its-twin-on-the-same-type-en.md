# DEC0024 | An opt-in `Linq` family swaps a method for its counterpart, when the counterpart is a twin on the same type

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-01 | Proposed | First draft | |
| 2026-11-01 | Accepted | At the maintainer's request | |

## Context

The catalogue has no family that touches a LINQ call. Stryker.NET has one, listed in its
documentation as 34 pairs it swaps - `Any` ↔ `All`, `First` ↔ `FirstOrDefault`, `Min` ↔ `Max`,
`Skip` ↔ `Take`, `OrderBy` ↔ `OrderByDescending`, `Sum` → `Max`, `Count` → `Sum`, `Average` →
`Min`, `Reverse` ↔ `AsEnumerable` among them. The documentation does not say whether it matches a call
by its name or by the method it binds to. Measured on `Reefact/slugger` (#15), it created 57 Linq
mutants and judged 16; #15 ranked the family "worth having, low priority".

On `Reefact/first-class-errors` at `8f84258` (#44, #53, #63), Stryker's Linq mutations gave 8 readable
verdicts in `FirstClassErrors.Analyzers`. Two `Any()` → `All()` survivors were confirmed by hand on a
clean clone: each swap passes the analyzers' 132 tests, because no test has one candidate that
satisfies the predicate and one that does not. The six `FirstOrDefault()` → `First()` survivors are
near-equivalent: five sit on `method.Locations.FirstOrDefault() ?? Location.None`, where a symbol
from source always has a location, and the sixth, at `ErrorContextKeyFacts.cs:43`, guards a `null`
that cannot occur because both `ErrorContextKey.Create<T>` overloads take `name` as their first
parameter.

The two confirmed gaps bind differently. At `DocumentedByInvalidSignatureAnalyzer.cs:47` the receiver
is an `ImmutableArray<IMethodSymbol>`, so `candidates.Any(...)` binds to
`System.Linq.ImmutableArrayExtensions.Any`, not to `System.Linq.Enumerable.Any`; at
`ExampleDoesNotCallDocumentedFactoryAnalyzer.cs:76` the call follows `.OfType<T>()` and binds to
`Enumerable.Any`. Roslyn's API returns `ImmutableArray` throughout. In the reference assemblies of
.NET 10.0.14, `System.Linq.ImmutableArrayExtensions` declares both `Any` and `All`, while
`System.MemoryExtensions` declares neither on `Span` or `ReadOnlySpan`, nor `Min` or `Max`. The same
repository declares a method of its own named `ErrorCodeFactory.Any()`, with no `All` beside it.

A method may return by reference - `ref bool Any()` - which `IMethodSymbol` reports apart from the
return type, as `ReturnsByRef` and `ReturnsByRefReadonly`. A `ref` local initialized from such a
call, `ref bool r = ref x.Any();`, does not compile from a call that returns by value.

Counted by name in that repository's production projects, not by binding: 13 `.Any(`, 11 of them with
a predicate, 9 `.OrderBy(`, 6 `.ThenBy(`, 2 `.Skip(`, 2 `.Except(`, 1 `.Concat(`, and no `.All(`,
`.Min(` or `.Max(`; against 15 `.FirstOrDefault(` and 8 `.First(`. The whole run on that repository
has 3048 mutants.

The catalogue already has what a family of this kind needs. Four mutators refuse a replacement that
would not bind, through `GetSpeculativeTypeInfo` (RB-011). `NumericLiteral` is opt-in, run when
`--mutators` names it or `--with` adds it, because "a literal is everywhere" and a project that did not
ask should not pay a run per literal (DEC0019); `Statement` entered the default catalogue because its
signal is high and its mutants compile (DEC0020). A LINQ call is an expression, and DEC0007's
`Hit<T>` records an expression's coverage as it is.

On 2026-11-01 the maintainer arbitrated three points: a call is swapped only when the counterpart
binds to a method of the same containing type with the same return type; the pairs are `Any` ↔
`All`, `Min` ↔ `Max`, `MinBy` ↔ `MaxBy`, `OrderBy` ↔ `OrderByDescending`, `ThenBy` ↔
`ThenByDescending`, `Order` ↔ `OrderDescending`, `Skip` ↔ `Take`, `SkipWhile` ↔ `TakeWhile`,
`SkipLast` ↔ `TakeLast`, `Union` ↔ `Intersect` and `Concat` ↔ `Except`; the family is opt-in first,
and its default status is decided after it has been measured. On review of the record, the maintainer
asked that the way the value is returned, by value or by reference, be part of the rule.

## Decision

In this context, we decide to add an opt-in `Linq` family that swaps a call's method name with its
counterpart from the eleven pairs above, when the counterpart binds to a method of the same
containing type as the original, with the same return type returned the same way.

## Rationale

Binding to a twin, rather than to `System.Linq.Enumerable` and `Queryable`, is what reaches both
confirmed gaps: one of them binds to `ImmutableArrayExtensions`, and a rule that names classes would
miss it, as it would miss any other library that declares the pair on a type of its own. The twin
rule names nothing and still keeps the
guarantee the issue asked for: `ErrorCodeFactory.Any()` has no `All` on its type and is left alone,
and a user type that does declare both names with the same shape is swapped as a pair the user made
comparable. Requiring the same return type, returned the same way, is what makes the mutant compile
by construction, as RB-011 does for operators: the call's value keeps its type wherever it is used,
and a `ref` return is never swapped for a value return, which a `ref` local initialized from the call
would refuse.

The eleven pairs are those whose two members have the same shape and whose swap changes what the call
computes - a quantifier, an extreme, an order, a side of a sequence, a set operation. The pairs left
out change something else: `First` ↔ `FirstOrDefault` and its kin swap an exception for a `null`,
which the sample shows near-equivalent six times out of six; `First` ↔ `Last` is equivalent on a
single-element sequence; `Sum`, `Count` and `Average` swaps change the type; `Reverse` ↔
`AsEnumerable` meets the `MemoryExtensions.Reverse` overload on arrays. On the sample, keeping the
eleven and dropping the rest keeps the two gaps and drops the six near-equivalents.

Opt-in first, because the family has never been measured by KillMutants: #15's 57 and the sample's 8
are Stryker's, created by a wider rule. The count by name suggests about thirty mutants on a
repository of three thousand, which is `Statement`'s profile rather than `NumericLiteral`'s, but a
family in the default catalogue changes every project's score, and DEC0020 put `Statement` there on a
measurement. Promoting a family is cheap; withdrawing one is not.

A family rather than an addition to an existing one, because no family swaps one method for another,
and `--with` and `--without` act on families.

## Alternatives considered

### Alternative 1 — No family (status quo)

* **Description:** leave LINQ calls unmutated, as #15 ranked the family low priority.
* **Why rejected:** two confirmed gaps on the sample, in analyzers, which no family in the catalogue
  can find: the call's value is a decision no operator family touches.

### Alternative 2 — Stryker's scope, matched by name

* **Description:** the 34 pairs, swapped wherever the name appears.
* **Why rejected:** on slugger that rule created 57 mutants and judged 16, and on the sample six of
  its eight verdicts were near-equivalents; matched by name, it would also swap a method the user
  wrote. The catalogue generates what compiles and asks a question, by construction.

### Alternative 3 — Bind to `System.Linq.Enumerable` and `Queryable` only

* **Description:** the issue's proposal: swap only a call bound to one of those two classes.
* **Why rejected:** it misses `ImmutableArrayExtensions.Any`, which is one of the two gaps that
  motivate the family, and with it any library that declares the pair on a type of its own.

### Alternative 4 — Default from the first version

* **Description:** enter the default catalogue as `Statement` did, on the strength of the count by
  name and of mutants that compile.
* **Why rejected:** the family has not been measured by KillMutants on any repository, and a default
  family changes every project's score; DEC0020 entered the default catalogue on a measurement.

## Consequences

### Positive

* A gap of the shape found twice on the sample - a quantifier that a test exercises on one side only
  - is reported, which no family could do.
* Every mutant the family proposes binds, with the call's type and its way of returning it unchanged,
  so none fails to compile.
* The rule reaches every type on which a library declares the pair, `ImmutableArray` included, and a
  user's own method is swapped only when its type declares the counterpart with the same shape.
* The default catalogue, and every score taken with it, are unchanged.

### Negative

* A project sees the family only by asking for it, with `--with Linq` or `--mutators Linq`.
* `First` ↔ `FirstOrDefault` and the other excluded pairs are not asked, so a gap of that shape is not
  found.
* A measurement on two repositories, and a second decision, stand between this version and the
  default catalogue.

### Risks

* A kept pair may still be near-equivalent in a given program: `Skip` ↔ `Take` on a sequence of one
  element, `Min` ↔ `Max` on a singleton, `OrderBy` ↔ `OrderByDescending` under a test that asserts
  membership only.
* A user type that declares both names of a pair with unrelated meanings yields a mutant that
  compiles and asks nothing.

### Follow-up actions

* Implement the family in the opt-in list, with the speculative binding and the twin rule, and its
  coverage through DEC0007's `Hit<T>` as for any expression.
* Update the catalogue table and the "not mutated" inventory in `architecture-en.md` and
  `architecture-fr.md`, the `README.md` and the CHANGELOG.
* Measure the family on `Reefact/slugger` and on `Reefact/first-class-errors` at `8f84258`.
* Decide its default status by a new record, on that measurement.
