# DEC0028 | The tool is published as `KillMutants`, and its command is `killmutants`

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-07 | Proposed | First draft | |
| 2026-11-07 | Accepted | At the maintainer's request | |

## Context

Nothing has been published yet. The milestone `1.0.0-rc.1` and issue #95 ask for a first
pre-release of the tool on nuget.org. A nuget.org package id cannot be changed once a version is
published under it: renaming afterwards means a second package and an unlisted first one, with the
consumers of the old id left behind. `RELEASING.md` names the first publish as the deadline for
settling the id.

Two documents disagreed on the name. `src/KillMutants.Cli/KillMutants.Cli.csproj` declares the
package id `KillMutants` and the command `killmutants`, and the README's installation section
documents that pair. The "Package naming" section of `RELEASING.md` proposed the id
`KillMutants.Cli` and the command `dotnet-killmutants`.

The other Reefact repositories give the bare name to the library and the `.Cli` suffix to the tool:
`DiagnosticCatalog` / `DiagnosticCatalog.Cli` (`dcat`), `JustDummies` / `JustDummies.Cli` (`dum`),
`FirstClassErrors` / `FirstClassErrors.Cli` (`fce`). Here the engine already carries a suffix of its
own, `KillMutants.Core`, on the `lib` train. The maintainer states that KillMutants is used through
its command line, and no consumer of the engine as a library has been identified. The nuget.org
trusted-publishing policy that `RELEASING.md` plans, and that is not configured yet, will
restrict this repository to the ids matching `KillMutants*`. The domain is
`killmutants.io`; the hyphen exists only in the GitHub slug `kill-mutants`. A nuget.org package id
cannot contain a space.

The `dotnet` driver runs `dotnet <foo>` by looking for a `dotnet-<foo>` command, for a tool pinned
in a repository's manifest as for one on the PATH. `RELEASING.md` measured the four combinations:
a command `dotnet-killmutants` is typed `dotnet killmutants` in both modes, while a command named
without the prefix is typed `dotnet <name>` when pinned and `<name>` alone when installed globally.
A tool pinned in a manifest is never on the PATH. Stryker.NET publishes its tool as `dotnet-stryker`
and is typed `dotnet stryker`.

A mutation score moves with the tool's version, which is why this organisation pins `dotnet-stryker`
per repository with `rollForward: false`, so that CI and a maintainer's machine measure the same
thing. The maintainer wants to type `killmutants` on its own, without `dotnet` in front. A
two-letter command, `km`, was raised and found too short by the maintainer.

## Decision

In this context, we decide that the tool is published on nuget.org under the package id
`KillMutants` and installs the command `killmutants`.

## Rationale

The product is the tool. KillMutants is used through its command line and nobody consumes the engine
as a library, so the bare name, the one a reader searches nuget.org for, goes to what is actually
installed. The sibling convention gives the bare name to a library because those repositories have
a library people use; this one does not, and its engine is already `KillMutants.Core`.

`killmutants` is the name typed in every mode: bare when installed globally, as the maintainer wants,
and after `dotnet` when pinned. The `dotnet` in front of a pinned tool is not a property of the name
- no choice of name removes it - so the prefix would buy only that the global form reads like the
pinned one, at the price of the bare invocation the maintainer asked for. Pinning stays the
recommended form for CI, for the reproducibility the organisation already relies on with Stryker.

One spelling, `KillMutants`, matches the domain, the package glob and the code; a spaced
`Kill Mutants` cannot be an id and would only add a second spelling to search for. A two-letter
command saves little once `dotnet` precedes it in CI and reads poorly in a script.

Both names are what the project file and the README already say, so the decision costs no rename,
only the rewrite of the section of `RELEASING.md` that argued otherwise, before anything is
published.

## Alternatives considered

### Alternative 1 — `KillMutants.Cli`, typed `dotnet killmutants` (the proposal in `RELEASING.md`)

* **Description:** follow the sibling convention for the id, and give the command the `dotnet-`
  prefix, so that one invocation, `dotnet killmutants`, holds whether the tool is pinned or global.
* **Why rejected:** it keeps the bare name for an engine nobody consumes as a library, and it takes
  away the bare `killmutants` the maintainer wants to type.

### Alternative 2 — `KillMutants`, typed `dotnet killmutants`

* **Description:** the bare id, with the `dotnet-killmutants` command for a single invocation.
* **Why rejected:** same reason on the command: one invocation everywhere is paid for with the bare
  `killmutants`, which is the form the maintainer asked for.

### Alternative 3 — A short command, `km`

* **Description:** install a two-letter command, in the manner of `dcat`, `dum` and `fce`.
* **Why rejected:** found too short by the maintainer; a short name pays only for a tool typed bare
  all day, and a pinned mutation tool is typed after `dotnet` in CI, where `dotnet km` reads poorly.

### Alternative 4 — `Kill Mutants`, in two words

* **Description:** name the product in two words, as a display name beside the id.
* **Why rejected:** a package id cannot contain a space, so the id would stay `KillMutants` and the
  product would carry two spellings to search for, against the domain and the code that already
  agree on one.

## Consequences

### Positive

* The name searched on nuget.org is the name installed, and the command is the product's name.
* A global install is typed `killmutants`, without `dotnet`.
* The project file and the README already say this: nothing is renamed before the first publish.

### Negative

* The engine can never take the bare `KillMutants` id; a library published later carries a suffix.
* The repository departs from the convention its siblings share.
* There are two invocations, `killmutants` and `dotnet killmutants`, and the documentation shows
  both.

### Risks

* A reader follows the global form in a CI script where the tool is pinned, or the reverse, and the
  command is not found.
* A library people would consume may appear later and find the bare name taken by the tool.

### Follow-up actions

* Rewrite the "Package naming" section of `RELEASING.md` to describe this decision.
* Reserve the `KillMutants.` prefix on nuget.org before the first publish.
