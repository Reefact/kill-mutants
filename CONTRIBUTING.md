# Contributing

Issues and pull requests are welcome. This file only points to where things are: it holds no rule
of its own, because two copies of a convention drift, and then nobody can tell which one is
current.

- **The conventions for proposing a change** are in **[`CLAUDE.md`](CLAUDE.md)**: commit subjects
  and bodies, the strictly linear history, the bilingual documentation, decision records, and the
  rule that a change to what the tool does arrives with the automated test that fails without it.
  The file is named for the agents that also work here, but its conventions bind every
  contributor, human or not. The one exception is in its *What you never do* section: merging a
  pull request, ruling on a decision record and tagging a release are the maintainer's calls, so
  those lines bind every contributor except the maintainer. The rest of that section binds
  everyone.
- **Building, testing, and the two tiers of CI** a pull request goes through are in the
  [Contributing section of the README](README.md#-contributing). `.github/workflows/ci.yml` holds
  the authoritative build and test invocation.
- **A vulnerability** is reported privately, never in a public issue: see
  [`SECURITY.md`](SECURITY.md).
- **Decision records**: which decisions earn one is in `CLAUDE.md`, and
  [`docs/decisions/README.md`](docs/decisions/README.md) says where records live and who accepts
  them.
