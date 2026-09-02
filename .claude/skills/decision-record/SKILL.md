---
name: decision-record
description: Think through and write a decision record (DECNNNN) for KillMutants — the two collaboration modes, the context/rationale/alternatives/consequences loops, the mandatory format and the final coherence check. Use when a decision needs to be challenged before it is recorded, drafted, superseded or reviewed, and whenever the maintainer asks for a decision record, an ADR, an IDR or a DEC.
argument-hint: "[the decision under discussion, or a DEC number]"
---

# Recording an important decision

This is the repository's method for capturing a decision so that a reader, months or years later,
understands why it was the right one **at the moment it was taken**.

It renders
[`Reefact/guidelines` → `important-decision-record-guideline.md`](https://github.com/Reefact/guidelines/blob/801615b78569eba80bf577a801d02a954819cbdc/important-decision-record-guideline.md)
at commit `801615b` (2026-09-01), because that repository is private and its readers here cannot open
it. **The guideline is the source of truth for the method**: where this file and the guideline
disagree on how a decision is thought through or written, the guideline is right and this file is the
defect. The repository's own conventions — where records live, how they are named, who accepts them —
are in [`docs/decisions/README.md`](../../../docs/decisions/README.md).

The two do not compete, because they answer different questions. The guideline says **how** a
decision is reasoned about and written down; DEC0001 says **which** decisions earn a record here at
all, and its test is the narrower one. Neither overrides the other.

Records here were called `ADR-NNNN` until 2026-09-02 and are now `DECNNNN`, as the guideline
specifies. The numbers did not change, so an older citation of `ADR-0003` means today's `DEC0003`.
Someone asking for "an ADR" is asking for a record.

## What a record is, and is not

It captures one important or structuring decision, technical or not. It is not limited to
architecture despite the identifier: the domain is open — product, organisational, human, strategic,
economic, regulatory, contractual — but the eligibility test is this repository's own, set by DEC0001,
and it is narrow. A decision
earns a record only when it is **hard to reverse** *and* admitted **more than one reasonable
answer**. A choice that follows obviously from the project's stated constraints is not recorded,
whatever its domain.

It is **historical memory, not living documentation.** It states what was true, known, expected or
decided on the day; those things may become false later without the record changing.

It is **not a specification.** No configuration, inventory, procedure or current state that would need
maintaining. A precise detail stays legitimate only when it is needed to understand the decision, or
when it *is* the decision.

**Utility filter**, applied to every sentence before it goes in: *does this help understand why this
decision was taken at that moment?* If not, it belongs to another document.

## The principles that never bend

* **One decision, one record.** If several independent decisions surface, say so and propose the
  split. If the maintainer keeps them together anyway, that arbitration is respected.
* **An accepted record is a historical trace: its content is never rewritten.** A decision that
  evolves becomes a *new* record; the old one's status gains a row saying it was superseded.
* **Invent nothing.** A hypothesis, an alternative, a consequence, a risk or a rephrasing raised while
  thinking becomes part of the record only once the maintainer validates it explicitly. Never invent a
  date, a status, a note, a linked record or a link. When a section has nothing to carry, say so —
  historical records use *"Not recorded at the time of the decision."*
* **Context holds facts, Rationale holds arguments drawn from those facts.** This strict separation is
  the heart of the method.
* **You draft and propose; the maintainer accepts.** Never accept, reject, deprecate or supersede a
  record, and never add a status row on your own authority.

## The two modes

### Exchange / Challenge — the default

This is the working mode until the maintainer **explicitly** asks for the write-up. Information
arrives in any order; the reflection is not linear even though the final record must be logically
coherent.

**Do not turn the target format into a questionnaire.** Analyse what is already known first, then ask
only the questions that are genuinely useful, grouping the coherent ones together.

While in this mode:

* distinguish facts from arguments;
* detect inconsistencies, ambiguities and missing information;
* challenge the real coherence stakes and the plausible blind spots;
* propose credible alternatives suited to the context;
* explore positive and negative consequences, risks and follow-up actions;
* let the reasoning evolve over successive passes.

Seek neither automatic approval (validating without challenging) nor contradiction on principle
(challenging for the sake of it). The goal is a solid record, not an exhaustive exercise.

**Local arbitrations.** The maintainer may close any point at any time. That point is then
*arbitrated*: stop challenging it spontaneously, but keep analysing its effects on the rest of the
record. The maintainer may reopen it at any time.

**Never block progress** on the grounds that a subject could be explored further. Flag what looks weak
or incomplete, then let the maintainer decide.

### Writing

Entering this mode is never your call: it happens only when the maintainer explicitly asks for the
record to be written or finalised. Then:

* unsolicited challenge stops;
* arbitrations already taken are not reopened;
* no new information is added;
* only content validated during the exchange is used;
* the mandatory format is respected strictly.

### Back to exchange

After a write-up the maintainer may reopen a subject. Return to Exchange / Challenge **on that point
only**, without re-litigating the rest.

## Construction loops

**Context ↔ Rationale.** The Rationale demonstrates that the decision fits, *from facts present in the
Context*. When an argument needs a fact that is not yet there: identify the missing fact, check it
with the maintainer, add it to Context if confirmed, and only then use it. Several passes are normal.

**Alternatives ↔ Context ↔ Rationale.** Look for enough *credible* alternatives, not an exhaustive
list: the point is enough coherent options to trust the arbitration. Where a situation already exists,
consider the **status quo** whenever it is a real option. If an alternative looks interesting but the
context cannot tell whether it is realistic, ask for the missing detail before retaining it. An
alternative becomes one of the record's only after explicit validation, and each retained alternative
carries an explicit reason for rejection.

**Consequences ↔ Alternatives ↔ Rationale ↔ Context.** Analyse four categories: *Positive* (benefits,
simplifications, capabilities unlocked), *Negative* (costs, constraints or drawbacks certain enough to
be the assumed price), *Risks* (unfavourable events that are possible but uncertain — never a certain
cost; short and plain, with no probability/impact formalism), *Follow-up actions* (migration,
communication, training, documentation, experiment, measurement, later review, support). Once the
negatives and risks are known, check that the decision is still preferable to the rejected
alternatives. If one now looks as good or better, challenge that — it may send you back to the
alternative, the rationale, the context or the decision itself.

## Mandatory format

No section is added, and none is removed. Copy
[`template-en.md`](../../../docs/decisions/template-en.md) and its French twin rather than retyping the
skeleton.

* **Identifier and title** — `DEC` plus four digits padded with leading zeros. The next number is the
  highest already present plus one; numbers are never reused and never renumbered, because they are
  cited from elsewhere in the documentation and from source comments. The title states the
  **decision**, never the question or the problem.
* **Status** — an append-only history, one row per state actually reached
  (`| Date | Status | Note | Related minutes |`). No existing row is ever modified or deleted.
  Statuses: *Proposed*, *Accepted*, *Rejected*, *Deprecated*, *Superseded by DECNNNN*.
* **Context** — facts only, of any relevant nature, forming the decision space without yet defending
  the chosen solution. Usually the longest section.
* **Decision** — **exactly one sentence**, present tense, active voice, self-contained. No context, no
  justification, no consequence, no comparison.
* **Rationale** — arguments only, each traceable to a fact in the Context. It never introduces new
  factual information.
* **Alternatives considered** — options genuinely considered and validated, each with an explicit
  reason for rejection.
* **Consequences** — Positive, Negative, Risks, Follow-up actions.

## Writing it into this repository

Records live in `docs/decisions/`, one decision per file, named `NNNN-kebab-case-summary-en.md` with a
French twin `NNNN-kebab-case-summary-fr.md`. **The English file is canonical**; the French one is a
translation that changes with it. Write both in the same pass — a record that exists in one language
only is an incomplete write-up.

Add one row per record to the index in **both** `README.md` and `README-fr.md`, and leave the status
there equal to the record's latest status row. When the record is worth surfacing to readers of the
repository, add it to the decision table in the root `README.md` too.

Then say plainly that the record is drafted as *Proposed* and awaits the maintainer's acceptance. Do
not add the acceptance row yourself.

## Final coherence check

Before calling a write-up final, verify that:

* every Rationale argument rests on a fact present in the Context;
* the alternatives are credible in that context, and the status quo was considered where relevant;
* every alternative has a clear reason for rejection;
* the negative consequences and risks do not make a rejected alternative preferable after all;
* anything discovered during these checks was folded back into the right place, usually the Context;
* nothing the maintainer has not validated is presented as settled;
* no specification or living-documentation detail slipped in;
* the Decision section holds one sentence and nothing but the decision;
* the French twin says the same thing as the English one, and both carry the same status history.

The conversation that leads to a record may be far richer than the document. The record keeps only
what is useful to a durable understanding of the decision, and validated.
