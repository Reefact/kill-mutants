# DEC0030 | A Dependabot pull request needs no Contributor Agreement while its head is Dependabot's own signed commit

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-09 | Proposed | First draft | |

## Context

DEC0029 accepts a pull request from anyone other than the Project Owner only once its author has
accepted the Contributor Agreement, by ticking a box in the description the pull request template
carries. A workflow, `contributor-agreement.yml`, refuses the pull request until the box is ticked.

The agreement defines a Contribution as material the contributor **intentionally submits**. It asks
that contributor to represent that they are legally entitled to submit it and hold the rights needed
to enter into the agreement, and it takes an assignment of the transferable economic rights in what
they submitted.

Dependabot is a GitHub app, not a legal person. `.github/dependabot.yml` tells it to watch the NuGet
packages and the GitHub Actions of this repository every week, and what it submits is a version
number answering an upstream release. On 2026-11-09 two of its pull requests were open, #111 and
#112. It does not fill the pull request template, so the box is never in the description it writes:
the check would fail on every one of its pull requests.

Anyone with push access can append commits to a Dependabot branch; opening a pull request does not
fix what that pull request later carries. A commit's author name is a local `git config` setting. The
head commit of a pull request Dependabot opens is signed by GitHub, as the heads of #111 and #112
were on 2026-11-09, with GitHub's own `web-flow` identity as its committer. GitHub verifies a
signature against a commit's committer, not its author: a commit whose author is Dependabot's
address, signed by someone else with their own key, also reads as verified, with Dependabot as its
author and that person as its committer. `[` and `]` cannot appear in a GitHub user name, so no one
else can register `dependabot[bot]`.

A Dependabot pull request stays Dependabot's whatever its branch later carries: its author on GitHub
is `dependabot[bot]` and its description is the one Dependabot wrote. A box ticked in that
description is ticked in the bot's pull request, and the check can attribute it to no one but
`dependabot[bot]`. Commenting `@dependabot recreate` on the pull request has Dependabot rebuild its
branch, dropping what was appended.

just-dummies, under the same Project Owner and the same agreement, settled this in its ADR-0090,
accepted on 2026-08-31, with the exemption this record proposes; the maintainer chose to align KillMutants
on just-dummies. That record weighed clearing the check by hand on each Dependabot pull request,
exempting every bot author, exempting Dependabot on its author alone, and making the check advisory
for bots. At any other head, ADR-0090 asks for the box like any other pull request does.

Reviewing the first drafts of this record and of the check on 2026-11-09, the maintainer asked that
a Dependabot pull request at any other head be refused rather than judged on that box, since ticking
it records no acceptance by the person who added the commit, or else that the acknowledgement be
made attributable to that person.

## Decision

In this context, we decide that a pull request opened by Dependabot needs no Contributor Agreement
acknowledgement while its head is Dependabot's own GitHub-signed commit, and is refused at any other
head, the commits added to it coming in through a pull request their author opens.

## Rationale

The check collects an assignment and a set of representations, and Dependabot can give neither: it
is not a legal person, and the version number it writes at this repository's own instruction carries
no third party's rights for it to assign. Demanding the box on its pull requests asks for a consent
nobody is in a position to give, and obtains one only because a human ticks it on the bot's behalf.

Waiving the acknowledgement is the direction in which a mistake costs something, so it rests on what
the branch carries rather than on who opened it. Anyone with push access can append to a Dependabot
branch, and what they append is a Contribution in the agreement's own terms; an author forges
freely, and so does a verified signature from any key but GitHub's own, while GitHub's signature
does not. Tying the exemption to a head that is still Dependabot's own signed commit withdraws it
the moment the branch carries anything else.

What the pull request then needs is the acceptance of whoever added the commit, and its own
description cannot hold one: a box ticked there is ticked in the bot's pull request and can only be
read as the bot's, which is the consent on another's behalf the first alternative below is rejected
for. A pull request their author opens is where that acceptance is theirs to give, so the commits go
there, and the Dependabot pull request is refused until it is back to Dependabot's own work.

The exemption is just-dummies' own, under the same owner and the same agreement. At any other head
this record departs from ADR-0090, for the reason above.

## Alternatives considered

### Alternative 1 — Tick the box by hand on each Dependabot pull request

* **Description:** leave the check as it is, and have the Project Owner edit the description of each
  Dependabot pull request to tick the box.
* **Why rejected:** it makes a manual bypass of a governance check the routine way dependency
  updates land, and ticks, on a bot's behalf, a consent nobody gave.

### Alternative 2 — Exempt every bot author

* **Description:** read the author account's type and waive the acknowledgement for any GitHub App.
* **Why rejected:** it hands the waiver to every app installed on the repository, now or later, on
  the strength of a type flag, which is far wider than the case that prompted it.

### Alternative 3 — Exempt Dependabot on its author alone, without the signed head

* **Description:** waive the acknowledgement whenever Dependabot opened the pull request.
* **Why rejected:** it waives it for whatever the branch carries rather than for what Dependabot
  wrote, and anyone with push access can append to the branch.

### Alternative 4 — Make the check advisory on bot pull requests

* **Description:** report the missing acknowledgement on a bot's pull request without failing it.
* **Why rejected:** the check collects a consent, and an advisory consent collects nothing: a check
  that reports "not accepted" and lets the merge happen anyway looks like protection without being
  one.

### Alternative 5 — At any other head, accept the box ticked in the Dependabot pull request

* **Description:** judge a Dependabot pull request that carries someone else's commit like any
  other, on the box in its description, as ADR-0090 does.
* **Why rejected:** the description is the bot's, so the box binds nobody and is ticked, in effect,
  on the bot's behalf; it records no acceptance by the person who added the commit.

### Alternative 6 — At any other head, collect the acceptance of the commit's author

* **Description:** keep the Dependabot pull request, and require an acknowledgement made by the
  person who added the commit, attributable to them.
* **Why rejected:** it adds a second way of accepting the agreement, tied to who pushed what, for a
  case a pull request of its own already handles with the one way that exists.

## Consequences

### Positive

* A Dependabot pull request is not held back by a consent it cannot give.
* No appended contribution rides in behind Dependabot's name: the exemption ends with the signed
  head.

### Negative

* One more API read on each event of a Dependabot pull request.
* A Dependabot pull request someone appended to is refused, even when the addition is trivial or is
  the Project Owner's own: the change goes in a pull request of its own.

### Risks

* Were GitHub to stop signing Dependabot's commits, every Dependabot pull request would be refused:
  noisy, and never in the direction that lets a contribution through without consent.

### Follow-up actions

Not recorded at the time of the decision.
