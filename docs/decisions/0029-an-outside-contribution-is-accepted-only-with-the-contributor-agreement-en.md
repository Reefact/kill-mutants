# DEC0029 | An outside contribution is accepted only with the Contributor Agreement, which assigns its economic rights to the Project Owner

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-09 | Proposed | First draft | |

## Context

KillMutants is owned by Sylvain Aurat, acting in his personal capacity (the Project Owner). Users
receive it under the PolyForm Internal Use License 1.0.0. Separately, the Project Owner grants
REEFACT, a French SARL, the REEFACT Exploitation License: REEFACT may use, modify and commercially
exploit the project, and may license and sublicense it to third parties under free,
source-available, proprietary, commercial or other models the Project Owner approves. That licence
grants what the Project Owner holds.

Code that someone else writes is theirs unless something says otherwise. The PolyForm licence grants
rights to the project's users; it is not a licence from a contributor to the Project Owner, and it
grants nobody the right to relicense.

On 2026-11-09, `CONTRIBUTING.md` and the README's Contributing section said that issues and pull
requests are welcome, and said nothing about the rights in what a pull request brings. No agreement
of any kind was asked of a contributor. The history on `main` then held commits from agents working
for the Project Owner and from Dependabot. Issue #102 had held `CONTRIBUTING.md` and a code of
conduct back on exactly this question: an invitation to contribute, before the rights are settled,
invites code the project might not be able to exploit. The OpenSSF Best Practices form (#92) asks for
a contribution guide.

just-dummies, another repository of the same Project Owner, under the same user licence and the same
exploitation licence to REEFACT, already settled the question. Its Contributor Agreement assigns to
the Project Owner, without payment, the transferable economic rights in a contribution, listed one by
one, worldwide and for their full legal duration, with a broad licence as a fallback where the
assignment is not legally available, a patent licence, and a consent on moral rights; it is
governed by French law. A contributor accepts it once per pull request, by ticking a box the pull
request template carries, and a workflow refuses the pull request until it is ticked. A pull request
the Project Owner opens directly needs no agreement; one from a `claude/*` branch, opened under the
Project Owner's account but written by an agent, keeps the box and ticks it.

A Developer Certificate of Origin certifies that the contributor may submit the code under the
project's licence. Here that licence is the PolyForm Internal Use licence.

A contributor who keeps their copyright can instead grant a broad, sublicensable licence, which
leaves them more and asks less of them.

The maintainer chose, on 2026-11-09, to align KillMutants on just-dummies.

## Decision

In this context, we decide that a pull request from anyone other than the Project Owner is accepted
only once its author has accepted, for that contribution, the KillMutants Contributor Agreement,
which assigns the contribution's economic rights to the Project Owner.

## Rationale

The exploitation licence lets REEFACT relicense the project under any model, and it can only grant
what the Project Owner holds. Accepting code without an agreement would leave parts of the project
that licence cannot reach. The agreement closes
that gap where it opens, on the pull request that brings the code.

A Developer Certificate of Origin would not close it: it certifies a right to submit under the
PolyForm licence, which carries no right to relicense. Turning the contributions away would close the
gap by closing the door, which the README and `CONTRIBUTING.md` say the project does not want.

Between an assignment and a licence, the same Project Owner already holds one model, with the same
two licences on both sides, in just-dummies. Using it here means one agreement to read, one set of
terms for a contributor who works on both, and one model to defend rather than two that differ for
no reason in the projects themselves.

## Alternatives considered

### Alternative 1 — Status quo: pull requests welcome, with no agreement

* **Description:** leave `CONTRIBUTING.md` and the README as they were, and merge outside
  contributions on the strength of the project's licence alone.
* **Why rejected:** the code merged that way stays its author's, out of reach of the exploitation
  licence, which is the problem #102 was opened on.

### Alternative 2 — Issues only, no outside code

* **Description:** accept issues, reports and vulnerability disclosures, and decline outside pull
  requests until the question is settled some other way.
* **Why rejected:** it closes the door on contributions the project says it welcomes, to avoid a
  question the agreement answers.

### Alternative 3 — A Developer Certificate of Origin alone

* **Description:** ask each commit for a `Signed-off-by:` line certifying the contributor's right
  to submit it.
* **Why rejected:** it certifies a right to submit under the PolyForm licence, which grants no right
  to relicense, so the gap stays open.

### Alternative 4 — A licence from the contributor, who keeps the copyright

* **Description:** a contributor licence agreement granting the Project Owner a perpetual,
  irrevocable, sublicensable licence, with the copyright left with the contributor.
* **Why rejected:** it would give KillMutants a model of its own beside the one just-dummies already
  holds for the same owner and the same two licences, and the maintainer chose to align the two.

## Consequences

### Positive

* A merged contribution can be relicensed, sublicensed to REEFACT and sold like the Project Owner's
  own code.
* `CONTRIBUTING.md` says on what terms a contribution is accepted, which #102 asked for.
* KillMutants and just-dummies ask a contributor for the same thing.

### Negative

* A contributor gives up the economic rights in their contribution, without payment; some will not
  contribute on those terms.
* Every pull request from a `claude/*` branch carries the box and ticks it.
* The check runs as `main` holds it, so a change to it is not exercised by the pull request that
  makes it.

### Risks

* The agreement is just-dummies' own text with the project's name changed: a defect in it is a
  defect in both projects.
* An acceptance given by ticking a box is contested by a contributor.
* The check is not made required in the `main-protection` ruleset, and a pull request merges without
  the agreement.

### Follow-up actions

* Make the `Contributor agreement` check required in the `main-protection` ruleset.
