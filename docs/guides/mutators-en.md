# The mutators

🌍 **Languages:**  
🇬🇧 English (this file) | 🇫🇷 [Français](./mutators-fr.md)

A **mutator** is a rule that rewrites one kind of code into one specific mistake. Mutators are
grouped into **families**, and each family asks your tests one question. This page lists the
sixteen families, what each one asks, how to choose which ones run, and what the catalogue leaves
alone on purpose.

## What a family asks

Every mutant costs a test run, so a family earns its place by the question it asks. Three examples
show the pattern.

**`Comparison` asks whether a boundary is tested.** `age >= 18` becomes `age > 18` — the boundary
shifted by one — and `age < 18` — the comparison negated. A test with 17 and 42 kills the second and
lets the first live; only a test at exactly 18 kills both.

**`LogicalOperator` asks whether each condition matters on its own.** `age >= 18 && hasConsent`
becomes `age >= 18 || hasConsent`. A suite that only ever passes an adult *with* consent cannot
tell the two apart; one test with consent withheld does.

**`Statement` asks whether a call's effect is observed.** `register.Add(age);` becomes `{ }`. A test
that calls `Register` and asserts nothing about the register passes either way.

The report's `By mutator` section shows, per family, how many mutants it produced and how many
your tests caught — the number to look at before deciding what to keep.

## The catalogue

Fifteen families run by default. `NumericLiteral` runs only when asked for.

| Family | Rewrites | Into |
| --- | --- | --- |
| `Comparison` | `>=` `>` `<=` `<` | the boundary shifted by one, and the comparison negated |
| `Comparison` | `==` `!=` | each other — there is no boundary to shift |
| `LogicalOperator` | `&&` `\|\|` | each other |
| `Arithmetic` | `+` `-` `*` `/` `%` | its counterpart |
| `Bitwise` | `&` `\|` `^` `<<` `>>` | its counterpart |
| `Assignment` | `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `<<=` `>>=` | its counterpart |
| `Increment` | `++` `--` | each other, prefix and postfix alike |
| `Conditional` | `c ? a : b` | `(c \|\| true) ? a : b` and `(c && false) ? a : b` — each branch forced, the condition still evaluated |
| `NullCoalescing` | `a ?? b` | `a`; and `b ?? a` when `a` only reads a variable, a field or an auto-property, otherwise `(a is null ? b : b)` — the fallback forced, `a` still evaluated |
| `BooleanLiteral` | `true` `false` | each other |
| `Negation` | `!x` | `x` |
| `NegatedDecision` | the condition of an `if`, a loop, or a returned `bool` | the decision negated — unless another family already makes that mutant of it |
| `Pattern` | `x is P`, `x is T`, and the patterns inside an `is` | the test negated; a relational pattern's boundary shifted and negated; each arm of an `or` or `and` dropped |
| `StringLiteral` | `"text"` | `""`, and `""` into a non-empty string |
| `Statement` | a statement that only calls something — `f();`, `await f();`, `x?.f();`, `new X();` — and a `yield return` | `{ }` |
| `Linq` | a call to one of eleven pairs — `Any`/`All`, `Min`/`Max`, `MinBy`/`MaxBy`, `OrderBy`/`OrderByDescending`, `ThenBy`/`ThenByDescending`, `Order`/`OrderDescending`, `Skip`/`Take`, `SkipWhile`/`TakeWhile`, `SkipLast`/`TakeLast`, `Union`/`Intersect`, `Concat`/`Except` | its counterpart, when the counterpart is a twin: same containing type, same return type |
| `NumericLiteral` *(opt-in)* | an integer literal initialising a constant, a field or a property | `n + 1` and `n - 1` |

Three properties hold across all of them, and they are what makes a verdict trustworthy:

* **A mutant is a new node, never a swapped token.** Roslyn emits from the node's kind, so
  changing only the operator's text would print `age > 18` while compiling `age >= 18` — a mutant
  that cannot be killed, reported as a survivor your tests never had a chance against.
* **A mutant that would not compile is never generated.** Each family asks the compiler whether
  its replacement binds before proposing it.
* **A mutant known to be equivalent is never generated.** `NullCoalescing` never silently drops a
  left operand that does something; `Conditional` skips a ternary whose two branches are the same
  expression; `NegatedDecision` never writes `!!x`.

### Three families worth a closer look

**`Conditional` forces each branch rather than swapping them.** `c ? a : b` becomes
`(c || true) ? a : b` and `(c && false) ? a : b`. The condition is still evaluated, so a side
effect in `c` still happens, and each mutant asks about one branch: *is there a test that needs
`b`?* then *is there a test that needs `a`?* Swapping the branches would ask both at once and be
killed by either ([DEC0017](../decisions/0017-a-ternary-is-forced-each-way-rather-than-swapped-en.md)).

**`NumericLiteral` is opt-in because literals are everywhere and most are not thresholds.**
`MinimumNouns = 100` becomes `101`, then `99`, which asks whether a test checks a threshold *at*
its boundary. It reaches a `const` only when the project itself reads it: a test assembly keeps
the value a constant had when it was compiled, so a constant only the tests read would survive
whatever they assert ([DEC0019](../decisions/0019-numeric-literals-are-an-opt-in-family-that-reaches-the-constants-its-project-reads-en.md)).

```bash
killmutants --with NumericLiteral       # the default families plus this one
killmutants --mutators NumericLiteral   # this one alone
```

**`Linq` names no class.** A call is swapped only when the counterpart binds to a twin of the method
called — declared on the same type, returning the same type. So `ImmutableArray`'s `Any` is reached
as `Enumerable`'s is, and a method of your own that merely shares a name is left alone
([DEC0024](../decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-en.md),
[DEC0027](../decisions/0027-linq-runs-by-default-and-without-leaves-it-out-en.md)).

## Choosing a catalogue

Not every family earns its keep on every project, and the report says which do. Measured on
KillMutants' own source, the operator families — `Comparison`, `LogicalOperator`, `Arithmetic` —
detect 45 % to 55 % of what they produce, while `StringLiteral` and `BooleanLiteral` together make
half of all mutants and detect 10 % to 15 %: error messages and flags nothing asserts on.

Those are true findings. On a project that asserts on its messages they are useful ones. So the
tool reports the split and leaves the choice to you:

```bash
killmutants --without StringLiteral,BooleanLiteral
killmutants --mutators Comparison,LogicalOperator,Arithmetic
```

| Option | Does |
| --- | --- |
| `--mutators <list>` | run **only** these families |
| `--with <list>` | add these to the default families — how an opt-in family is turned on |
| `--without <list>` | leave these out, applied after the two above; `--without none` restores every family a `killmutants.json` excludes |

**A score is only comparable with another score from the same catalogue.** Dropping those two
families on this repository took a run from 413 mutants in 7.1 minutes to 207 in 4.1, and the
score from 28.81 % to 43 % — not because the tests improved, but because a different question was
asked. Pick a catalogue for a CI job, write it in [`killmutants.json`](./configuration-en.md) so it
is versioned with the code, and keep it. The JSON report lists the families that actually ran
under `byMutator`, so a consumer can tell which question was answered.

## Silencing calls you do not want to hear about

A family is a blunt instrument for a finer choice. Measured on one repository, `Statement` left 144
survivors, 116 of them argument guards — `ArgumentNullException.ThrowIfNull(x)` and its siblings,
covered by tests that never pass `null` — burying 9 real gaps.

`--ignore-calls` names the calls a project does not want findings about, by the fully qualified
name of the method the call **binds to**:

```bash
killmutants --ignore-calls 'System.ArgumentNullException.ThrowIfNull' \
            --ignore-calls 'System.ArgumentException.ThrowIf*' \
            --ignore-calls '*.ConfigureAwait'
```

* `*` and `?` work as in `--exclude`; case matters, as it does in C#.
* A constructor is `System.ArgumentException.ctor`.
* A bare `ThrowIfNull` recognises nothing — a method of your own that shares the name is not
  caught — and the report says a rule that silenced nothing silenced nothing.
* A recognised call silences its statement and its arguments — `ConfigureAwait(false)`'s literal,
  an exception's message — and nothing beyond: not what the call is made on, and not the body of
  a lambda or local function handed to it, which is code of its own.

A silenced mutant is still generated, never tested, and reported as **ignored**: outside the score,
counted per family and per rule, with the count printed beside the score itself. The choice is the
project's, it is versioned in `killmutants.json` as `ignoreCalls`, and the report shows it rather
than hiding it ([DEC0026](../decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-en.md)).

## Leaving code alone

Three mechanisms keep code out of a run, and they answer three different questions.

**`--exclude <pattern>`: this is not code I want measured.** Fixtures, samples, generated files. The
pattern is matched against the path relative to the directory being scanned; `*` matches `/` too,
so `tests/*` covers everything beneath `tests`. An excluded *project* is left out of the run
entirely; an excluded *file* is still compiled but never mutated.

```bash
killmutants --exclude "tests/fixtures/*" --exclude "*.Generated.cs"
```

**`[ExcludeFromCodeCoverage]`: this code is not part of what the tests are expected to cover.** The
attribute already says so, and KillMutants takes it at its word: nothing under it is mutated.

**`<KillMutantsTestSupport>true</KillMutantsTestSupport>`: this project is scaffolding.** Builders,
fakes, clocks, assertion helpers — a test-support library is not the subject, and mutating it
reports findings nobody set out to measure. Nothing structural tells such a library apart from the
code under test, so it says so itself, in its `.csproj`:

```xml
<PropertyGroup>
  <KillMutantsTestSupport>true</KillMutantsTestSupport>
</PropertyGroup>
```

The project is then left out without hiding what it references: the code under test behind it is
still found and still mutated, and a helper that happens to be a runnable test application is not
launched as a suite either.

## What is deliberately not mutated

The catalogue is selective rather than exhaustive. Every entry below was measured by running the
catalogue over the form, not read off the language specification.

| Not mutated | Why |
| --- | --- |
| `+x` | removing a unary plus changes nothing: the mutant is equivalent by construction |
| `~x` | any test touching the expression kills it; nearly free to write, nearly worthless |
| `?.`, `as` | both mutate into forms that throw rather than compute, so they measure whether a test touches the line, not whether it checks the result |
| the patterns of a `switch` | changing one arm can make a later arm unreachable, which the compiler refuses for the whole switch ([DEC0018](../decisions/0018-patterns-are-mutated-by-a-family-of-their-own-en.md)) |
| `switch` arms | reordering or removing arms is a structural mutation, which needs reasoning about exhaustiveness |
| other LINQ swaps — `First` ↔ `FirstOrDefault`, `First` ↔ `Last`, `Sum`, `Count`, `Reverse` | near-equivalent where measured, or a change of type ([DEC0024](../decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-en.md)) |
| `>>>`, `^=`, `-x`, numeric literals outside an initializer, assignments and control flow as statements | **future candidates**, each with a reason recorded in the [architecture](../architecture-en.md) |

An expression that *declares* a variable — a pattern variable, an `out` argument — is not mutated
either: changing when its parts are evaluated breaks definite assignment, and the project stops
compiling. The call around it is still mutated.

## Where to go next

| If you want to… | Read |
| --- | --- |
| keep the catalogue in a file beside the code | [Configuration](./configuration-en.md) |
| read the per-family split in the report | [Reading a report](./reading-a-report-en.md) |
| know why the catalogue is selective rather than complete | [Design principles](./design-principles-en.md) |

---

[← Documentation index](../README.md)
