# Agent instructions

The conventions for this repository are in **[`CLAUDE.md`](CLAUDE.md)**. Read it before proposing
any change.

There is one file rather than two because two copies of a convention drift, and then neither the
reader nor the writer can tell which one is current. `CLAUDE.md` is short, and it holds precisely
the rules that nothing in this repository enforces — so nothing will stop you breaking them, and no
failing build will tell you afterwards.

The list below is the one part repeated here, because each item is irreversible and the cost of
learning it late is not a red build but a damaged history. It is a summary; `CLAUDE.md` is the
source.

- **Never merge a pull request, and never push to `main`.**
- **Never accept a decision record**, and never add a status row to one. You draft and propose; the
  maintainer accepts.
- **Never rewrite an accepted decision record.** A decision that evolves becomes a new record.
- **Never renumber a `DECnnnn` or an `RB-nnn`.** They are cited from source comments; a number is a
  permanent handle.
- **Never tag a release.** See `RELEASING.md`.
- **Never `git merge` into a pull request branch.** `main` is strictly linear; rebase instead.
