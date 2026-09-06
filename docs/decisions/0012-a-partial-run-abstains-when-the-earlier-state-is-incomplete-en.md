# DEC0012 | A partial run withholds its verdict over an incomplete earlier state

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-09-06 | Proposed | First draft | |
| 2026-09-06 | Accepted | | |

## Context

A partial run compares the working tree against an earlier state, which it reconstructs in a detached
worktree. Components tracked separately - submodules, to git - are laid out from the object store the
clone already holds. A component whose objects are not present locally cannot be laid out at all: a
fresh clone before `submodule update --init` has the gitlink and nothing behind it, and no amount of
local work produces the objects. The snapshot names such a component rather than leaving an empty
directory, because an empty directory reads as an answer: no such project, therefore no edge,
therefore nothing lost.

Until this decision, that list reached exactly one place. The traversal refused to continue when it
was asked for a project sitting *inside* a missing component. Every other run went to a verdict as if
the comparison had been complete.

Measured on an SDK project: a project outside a missing component can import a build file from inside
it, and MSBuild skips an import whose `Exists()` is false in silence. On the same project,
`-getItem:ProjectReference` answers with the reference when the component is present and with `[]`
when it is not - success, no warning, exit code 0 in both cases. The references that file would have
added are gone from the reconstructed graph, and nothing in the evaluation records that they were
ever expected. The importing project is not under the missing path, so a check keyed on containment
cannot reach it either.

The verdict already had two reasons to withhold a pass: a project the change left with no test
reaching it, and a selection whose mutants were all untestable. Both rest on the same rule, stated in
DEC0009 and applied again in DEC0010: a run that established nothing must not report success.

The project's governing principle is that a measuring tool may under-detect, never lie. A partial
run's verdict is what a continuous integration gate acts on.

## Decision

When a partial run's snapshot reports any component it could not read, the run completes and reports
what it found, and its verdict withholds a pass.

## Rationale

The relevance of a missing component cannot be established from what remains. The measurement shows
the failure is silent at the point where it would have to be detected: MSBuild returns success and a
shorter list, so there is no signal to narrow on. Any rule that tried to refuse only when the missing
component mattered would be guessing, and guessing wrong means passing.

Withholding the verdict is the only response that costs nothing but the pass. The findings the run
produced were produced by really running mutants against really built code, and they are worth
reading whatever the comparison could not establish; ending the run would throw them away. Reporting
the gap while still passing would leave a green for a gate to act on, which is the failure the
governing principle names.

It also needs no configuration, and therefore has nothing that can be turned off later and left off.

The verdict already withholds a pass for two conditions of the same shape, so this is a third reason
in an existing mechanism rather than a new kind of outcome.

## Alternatives considered

### Alternative 1 — Refuse the partial run outright

* **Description:** stop the run as soon as the snapshot reports anything missing, with a message
  naming the components and the command that fetches them.
* **Why rejected:** it is exactly as safe and strictly less useful. The run has already done work
  worth reporting, and refusing discards it for no additional protection - the pass is withheld
  either way.

### Alternative 2 — Report the gap and let the verdict stand

* **Description:** name the unread components in the report and otherwise decide the verdict from the
  mutants as usual.
* **Why rejected:** a green with a footnote is still a green, and a gate acts on the exit code rather
  than on the prose. It would also ask the reader to judge whether the missing component mattered,
  which is the one question the run could not answer.

### Alternative 3 — Withhold the verdict only when the missing component is relevant

* **Description:** establish whether anything reached into the missing component and abstain only
  then.
* **Why rejected:** not decidable. The measurement above shows a skipped import leaves no trace in
  the evaluation, so relevance would have to be recovered by reading build files textually - which
  this project already declined for the project graph, because an import can be computed, inherited
  from an SDK or nested. A scan that misses one goes green, which is the failure being removed.

### Alternative 4 — Let the developer choose the risk

* **Description:** a flag allowing a run to pass over an incomplete comparison, either globally or by
  naming the component vouched for.
* **Why rejected:** the choice would not be informed. Deciding that a missing component is harmless
  requires knowing whether anything imports from it, which is precisely what cannot be seen while it
  is missing, so the developer would be asserting a belief rather than acting on evidence. The named
  form is the better of the two, since a claim about one component is reviewable and a new component
  does not inherit an old permission, and it stays available if use ever calls for it.

## Consequences

### Positive

* A partial run can no longer report success over a comparison it did not establish.
* The report survives: the mutants that were run are still listed, so a developer whose clone is
  incomplete still learns something rather than only being refused.
* There is nothing to configure, so there is nothing to switch off during an incident and leave off.
* The refusal that used to fire when the traversal walked into a missing component becomes
  unnecessary, and with it an inconsistency: the same repository in the same state ended in an
  exception or in a full report depending on what the diff happened to touch.

### Negative

* A repository whose continuous integration does not initialise its components cannot use `--since`
  as a gate until that workflow changes, because the pass is unobtainable.
* With the containment refusal gone, a project reported as having lost coverage may be an artifact of
  an edge that could not be read. The report therefore has to say which of its parts the
  incompleteness reaches rather than presenting them all as established.

### Risks

* "No verdict" may be read as a defect in the tool rather than as a statement about the clone, and
  answered by dropping `--since` instead of by fetching the components.
* A repository that deliberately leaves a large component uninitialised most of the time would never
  obtain a pass from `--since`, and nothing in this decision offers it a way through.

### Follow-up actions

* The root `README.md` documents the two conditions under which a partial run fails and does not yet
  mention this third one. It is added in the same change as this record.
