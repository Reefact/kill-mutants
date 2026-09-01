# Releasing

A release is a **tag push**. Nothing is published from a developer's machine, and no
step of the publish path lives outside this repository.

## Release trains

The engine and the tool version independently. Each owns a tag prefix; the
authoritative mapping is [`tools/trains.sh`](tools/trains.sh).

| Train | Tag           | Publishes                   |
| ----- | ------------- | --------------------------- |
| `lib` | `lib-v1.2.3`  | the KillMutants engine      |
| `cli` | `cli-v1.2.3`  | the KillMutants CLI (a .NET tool) |

A project joins a train by declaring it in its own `.csproj` — nothing else:

```xml
<PropertyGroup>
  <ReleaseTrain>cli</ReleaseTrain>
</PropertyGroup>
```

That declaration is what makes the project packable, gives it an embedded SBOM, and
puts it on the train (see [`Directory.Build.targets`](Directory.Build.targets)). There
is no second list to keep in sync, so a renamed or moved project cannot fall out of its
own release.

Across trains, depend through a **published `PackageReference`**, never a
`ProjectReference`: `dotnet pack` would stamp the dependency at the version being
packed, which was never published. `pack.sh` refuses such a release.

## One-time setup

Until both are done, every run of `release.yml` — dry run included — fails at the
NuGet login step, by design.

**1. The `NUGET_USER` repository variable** → the nuget.org account username (the
profile name, not the email). A *variable*, not a secret: it is an identifier, not a
credential, and masking it would only make a failed login harder to read.

**2. A trusted-publishing policy on nuget.org** (your username → *Trusted Publishing*):

| Field | Value | |
| ----- | ----- | - |
| Repository Owner | `Reefact` | |
| Repository | `kill-mutants` | |
| Workflow File | `release.yml` | The file NAME only — no `.github/workflows/` path. A mistyped name matches nothing and fails the login on a real tag. |
| Environment | *(empty)* | `release.yml` declares no `environment:`. Filling this in requires the token to carry that claim, and the push fails. |
| Scopes | Push new packages and package versions | Nothing is published yet; "only new package versions" would refuse the first publish of a new package id. |
| Glob Patterns | `KillMutants*` | Limits what this workflow may push, and covers every package id this repository will own (see "Package naming" below). Not `*`, which would let a compromised repository publish to every package the account owns. |

No long-lived API key is stored anywhere: the OIDC exchange mints a short-lived,
single-use key, valid for an hour, which is why `release.yml` logs in immediately
before the push rather than at the top of the job.

A new policy may start *temporarily active for 7 days* — typically on a private
repository — and goes inactive if nothing publishes in that window (it can be
restarted at any time). nuget.org needs the GitHub repository and owner ids, which
only a successful token exchange supplies, to lock the policy against resurrection
attacks. A dry run is enough to satisfy it: see "Rehearsing" below.

## Package naming

Following the convention every Reefact repository already shares — `DiagnosticCatalog`
/ `DiagnosticCatalog.Cli` (`dcat`), `JustDummies` / `JustDummies.Cli` (`dum`),
`FirstClassErrors` / `FirstClassErrors.Cli` (`fce`):

| Train | `PackageId` | `ToolCommandName` |
| ----- | ----------- | ----------------- |
| `lib` | `KillMutants.Core` | — |
| `cli` | `KillMutants.Cli` | `dotnet-killmutants` — typed `dotnet killmutants` |

The repository name is kebab-case and the package ids are PascalCase, suffixed with a dot
(`KillMutants.Xunit`, ...). No package id carries a hyphen, which is why one glob covers
them all.

One deliberate departure, for now: the sibling repositories give the engine the BARE name
(`DiagnosticCatalog`, `JustDummies`, `FirstClassErrors`) and suffix only the satellites,
whereas the engine here is `KillMutants.Core`. Nothing in the pipeline cares — the glob,
the trains and the workflows are all written against ids they never spell out — so `Core`
can stay, or a bare `KillMutants` can join or replace it later, with no release change.

The deadline for settling it is the first `lib-v*` tag, not the next merge. A nuget.org
package id is immutable: renaming in the source tree is a refactor, renaming after a
publish is not a rename at all — it is a second package plus an unlisted first one, and
consumers of the old id are left on a dead end. Before anything is published the choice
is free; after, it is permanent.

Worth doing early either way: reserve the `KillMutants.` prefix on nuget.org (prefix
reservation, for a verified owner). The package ids match a domain the project owns, and
an id nobody has claimed is an id anybody can claim.

`PackageId` and `ToolCommandName` are independent properties, and the convention uses
that: you install `DiagnosticCatalog.Cli` and you type `dcat`. The short name belongs to
the COMMAND, never to the package.

Stryker.NET does the opposite — `Stryker.Core` publishes as `stryker`, `Stryker.CLI` as
`dotnet-stryker` — so its package ids match none of its project names. The `dotnet-`
prefix there is not decoration and not a leftover: the `dotnet` driver dispatches
`dotnet <foo>` to a `dotnet-<foo>` command, which is why Stryker is invoked as
`dotnet stryker` everywhere, this organisation's own mutation workflows included.

### Why this tool takes the prefix and the others do not

`dcat`, `dum` and `fce` are installed GLOBALLY, which puts the command on PATH and lets a
short name be typed bare — `fce init`. That is what makes short pay there.

A mutation tool is not installed that way. Scores move with the tool's version, so it is
pinned per repository through `.config/dotnet-tools.json` — exactly how this organisation
already pins `dotnet-stryker` at 4.16.0 with `rollForward: false`, so CI and a
maintainer's machine measure the same thing. A pinned tool is invoked as
`dotnet <command>`, and the two shapes then diverge (all four rows measured, not assumed):

| `ToolCommandName` | pinned locally | installed globally |
| ----------------- | -------------- | ------------------ |
| `dotnet-killmutants` | `dotnet killmutants` | `dotnet killmutants` |
| `kmut` | `dotnet kmut` | `kmut` (and `killmutants` is not found) |

The prefixed name gives ONE invocation that holds in both modes; a short name gives two,
and the documentation has to branch on how the reader installed it. That is the whole
reason for the choice — not imitation of Stryker, though Stryker made the same call, and
`dotnet stryker` followed by `dotnet killmutants` reads as one pipeline.

The prefix is not decoration: the `dotnet` driver dispatches `dotnet <foo>` to a
`dotnet-<foo>` command, for a manifest-pinned tool as well as one on PATH.

No hyphen, matching the product's own name: the repository slug is `kill-mutants`, but the
domain is `killmutants.io` and the packages are `KillMutants*`. The hyphen lives only in
the GitHub slug — the same split this organisation already has between `just-dummies`,
`justdummies.io` and `JustDummies`.

## Cutting a release

1. Land the change on `main`. The release refuses any commit that is not an ancestor of
   `main`, so a tag cannot bypass branch protection.
2. Move the entry from `## [Unreleased]` to a dated heading — `## [1.2.3] - 2026-09-01`.
   The release refuses a version the changelog does not document.
3. Tag and push:
   ```sh
   git tag cli-v1.2.3 && git push origin cli-v1.2.3
   ```

That is the whole procedure. The workflow then resolves the version from the tag,
verifies the changelog, builds, tests, packs only that train, attests the artifacts,
pushes to NuGet, and publishes a GitHub Release with train-scoped notes.

## When a release fails

Delete the tag. A run that failed before publishing leaves a tag describing a release that did
not happen, and `release-notes.sh` reads tags as the record of what shipped — so the next
release would start its notes from that tag and omit every commit up to it, changes no consumer
ever received.

```sh
git push --delete origin lib-v1.2.3 && git tag -d lib-v1.2.3
```

Then fix the cause and tag again. Re-tagging the SAME version is safe only while nothing was
published under it; once nuget.org has the version, it is immutable — release the next one
instead. A run that failed AFTER the NuGet push falls in that second case: the packages are out,
so keep the tag and go forward.

## Rehearsing

- **Every pull request** runs `release-dryrun`: build, pack every train, embed the SBOM,
  run the packaging guards. No attestation, no login, no push, no tag.
- **On demand**, `release.yml` → *Run workflow* with `dry_run` ticked (the default) runs
  the *full* path including the provenance attestation and the OIDC token exchange, and
  skips only the two steps that publish. Use it to prove the trusted-publishing policy
  works before a real tag depends on it.

## Verifying a published package

The provenance attestation covers the bytes attached to the **GitHub Release**:

```sh
gh attestation verify KillMutants.Cli.1.2.3.nupkg --repo Reefact/kill-mutants
```

The nuget.org copy deliberately does not match that checksum — nuget.org repository-signs
every upload — so verify that one with `dotnet nuget verify` instead.

## Why not the usual approach

The reference implementation in this space, Stryker.NET, releases by running a Node
script on a maintainer's laptop that rewrites the version in three files, commits, tags
and pushes; an Azure DevOps pipeline then packs from `master`, and a human starts and
approves a "Production" environment whose `nuget push` configuration is not in the
repository at all. It works, and it has four properties this pipeline is built to avoid:

- **the version lives in several places**, kept in step by string replacement — here it
  is derived from the tag and exists nowhere else;
- **the publish step is invisible** to review, history and blame — here the whole path
  is `release.yml`;
- **a long-lived API key** authorises the push — here an OIDC exchange mints a
  short-lived, single-use one;
- **nothing binds the published bytes to a commit** — here every artifact carries a
  signed SLSA provenance attestation and an SPDX SBOM.
