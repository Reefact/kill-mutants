# Decision records

*🇫🇷 [Version française](README-fr.md)*

> Maintainer documentation. This base is the repository's memory of **why**, and it is not part of
> any documentation describing **what** the tool does today.

Records are numbered `DECNNNN` and live in this folder. They were called `ADR-NNNN` in `docs/adr`
until 2026-09-02, when the base moved to the writing method's own identifier; the numbers themselves
did not change, so `ADR-0003` and `DEC0003` are the same record. Identifiers are cited from
`README.md`, `docs/architecture-*`, `docs/robustness-backlog-*`, `docs/study/*` and from source
comments, so a number, once assigned, is a permanent handle.

## What a record is

A record captures one important or structuring decision and lets a reader, months or years later,
understand why that decision was the right one **at the moment it was taken**.

It is **historical memory, not living documentation.** It states what was true, known, expected or
decided on the day. Any of it may stop being true later without the record changing a word.

## The rules that actually bite

* **One decision, one record.** When a discussion turns out to carry several independent decisions,
  that is said out loud and a split is proposed. Keeping them together stays the maintainer's call.
* **An accepted record is never rewritten.** A decision that evolves is a *new* record; the old one's
  status gains a row saying it was superseded. Its body is history.
* **Decision is exactly one sentence** — present tense, active voice, self-contained. No context, no
  justification, no consequence, no comparison.
* **Context holds facts, Rationale holds arguments drawn from those facts.** An argument that needs a
  fact absent from Context does not get to smuggle it in: the fact is checked with the maintainer,
  added to Context, and only then used.
* **Nothing is invented.** A hypothesis, an alternative, a consequence or a risk raised while
  thinking becomes part of a record only once the maintainer has validated it explicitly — dates,
  statuses and links included. Where a historical record says nothing, the section says so rather
  than filling the gap.
* **A record is not a specification.** No configuration, inventory, procedure or current state that
  would need maintaining. The filter for any sentence: *does this help understand why the decision
  was taken then?* If not, it belongs to another document.

**One exception, recorded here rather than left implicit.** On 2026-09-02 the nine records that
already existed were reformatted into this format and renumbered from `ADR-NNNN` to `DECNNNN`, while
they were accepted. That was a bootstrap migration of the base's presentation, not a reconsideration
of anything: wording moved between sections, sections with no recorded material say so rather than
being filled, and no status row was added because no decision changed state. Two decisions do read
differently, and both changes are the migration itself — DEC0001 names the folder and the identifier
that were renamed, and DEC0009 was rewritten into the single sentence the format requires, its table
of exit codes being user documentation that already lives in the repository's `README.md`. The rule
above governs the base from that day on. A base nine records old was the last moment at which
converting them cost less than living with two formats for good.

## Where the format comes from

The method — the two collaboration modes, the construction loops, the mandatory format and the final
coherence check — is
[`Reefact/guidelines` → `important-decision-record-guideline.md`](https://github.com/Reefact/guidelines/blob/801615b78569eba80bf577a801d02a954819cbdc/important-decision-record-guideline.md),
at commit `801615b` (2026-09-01).

**That repository is private**, so a contributor or an agent working in this one cannot open it, and
a pointer to a document its reader cannot read is not an instruction. The guideline is therefore
rendered inside this repository, as the `decision-record` skill under
[`.claude/skills/decision-record/`](../../.claude/skills/decision-record/SKILL.md). That rendering is
a delivery mechanism, never a second source of truth: where the two disagree on method, the guideline
is right and the skill is the defect. The guideline says how a decision is reasoned about and written;
which decisions earn a record here at all is DEC0001's to say, and the two do not compete.

## File conventions

* One decision per file, named `NNNN-kebab-case-summary-en.md`, with a French twin
  `NNNN-kebab-case-summary-fr.md`. **The English file is canonical**; the French one is a translation
  that changes with it, carrying the same number, the same status history and the same content.
* The title names the **decision**, never the question or the problem.
* **No section is added to the format, and none is removed.** Every record carries exactly Status,
  Context, Decision, Rationale, Alternatives considered and Consequences, and Consequences carries
  exactly Positive, Negative, Risks and Follow-up actions.
* **Status is an append-only history** — one row per state the decision actually reached, and no
  existing row is ever edited or deleted. Statuses in use: *Proposed*, *Accepted*, *Rejected*,
  *Deprecated*, *Superseded by DECNNNN*.
* A supersession is written on both sides: the superseded record gains a status row naming its
  successor and nothing else about it changes; the successor names what it replaces in its own
  Context.
* Records are cited by identifier from elsewhere in the documentation and from source comments.
  Renumbering one breaks those citations silently, so numbers are permanent handles.

## Who proposes, who accepts

An agent — or anyone preparing a record — **drafts and proposes**. It never accepts, rejects,
deprecates or supersedes a record, and never adds a status row on its own authority. That is the
maintainer's call, and it is deliberately the same boundary that keeps an agent from merging a pull
request.

## Index

| DEC | Title | Status |
|---|---|---|
| [DEC0001](0001-record-architecture-decisions-en.md) | Record architecture decisions | Accepted |
| [DEC0002](0002-one-compilation-per-mutant-en.md) | One compilation per mutant | Accepted |
| [DEC0003](0003-compilation-inputs-from-csc-command-line-en.md) | Take compilation inputs from MSBuild's `csc` command line | Accepted |
| [DEC0004](0004-run-tests-by-launching-the-test-executable-en.md) | Run tests by launching the test project's executable | Accepted |
| [DEC0005](0005-verify-the-baseline-before-mutating-en.md) | Verify the baseline through the mutation path before mutating | Accepted |
| [DEC0006](0006-identify-tests-by-name-not-by-unique-id-en.md) | Identify tests by name, not by their unique id | Accepted |
| [DEC0007](0007-measure-coverage-with-a-type-preserving-probe-en.md) | Measure coverage with a type-preserving probe, one test at a time | Accepted |
| [DEC0008](0008-never-reuse-a-test-host-between-mutants-en.md) | Never reuse a test host between mutants | Accepted |
| [DEC0009](0009-exit-codes-are-a-public-contract-en.md) | Exit codes are a public contract | Accepted |
| [DEC0010](0010-a-partial-run-reports-findings-not-a-score-en.md) | A partial run reports findings, not a score | Accepted |
| [DEC0011](0011-widen-a-partial-run-selection-when-a-test-file-changes-en.md) | Widen a partial run's selection when a test file changes | Accepted |
