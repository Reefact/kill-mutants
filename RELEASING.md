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

Settled by [DEC0028](docs/decisions/0028-the-tool-is-published-as-killmutants-and-its-command-is-killmutants-en.md),
before the first publish:

| Train | `PackageId` | `ToolCommandName` |
| ----- | ----------- | ----------------- |
| `lib` | `KillMutants.Core` | — |
| `cli` | `KillMutants` | `killmutants` |

The repository name is kebab-case and the package ids are PascalCase, suffixed with a dot
(`KillMutants.Xunit`, ...). No package id carries a hyphen, which is why one glob covers
them all. The hyphen lives only in the GitHub slug: the domain is `killmutants.io` and the
packages are `KillMutants*` — the same split this organisation already has between
`just-dummies`, `justdummies.io` and `JustDummies`.

A nuget.org package id is immutable: renaming in the source tree is a refactor, renaming
after a publish is not a rename at all — it is a second package plus an unlisted first one,
and consumers of the old id are left on a dead end. That is why the names were decided
before anything was published, and why changing either one now takes a new record, not an
edit here.

Worth doing before the first tag: reserve the `KillMutants.` prefix on nuget.org (prefix
reservation, for a verified owner). The package ids match a domain the project owns, and
an id nobody has claimed is an id anybody can claim.

### Why the tool takes the bare name

The sibling repositories give the bare name to the library and suffix the tool —
`DiagnosticCatalog` / `DiagnosticCatalog.Cli` (`dcat`), `JustDummies` / `JustDummies.Cli`
(`dum`). This one departs from that on purpose: KillMutants is used through its command
line, nobody consumes the engine as a library, and the engine already carries a suffix of
its own. The bare name, the one a reader searches nuget.org for, goes to what is actually
installed. The price is permanent: a library published later carries a suffix.

### Why the command carries no `dotnet-` prefix

The `dotnet` driver dispatches `dotnet <foo>` to a `dotnet-<foo>` command, for a
manifest-pinned tool as well as one on PATH, and a pinned tool is never on PATH. The shape
of the invocation therefore depends on how the tool was installed. The four shapes were
measured with a short unprefixed name, `kmut`; the second row is that measurement with this
tool's name in place, since the driver's rule looks at the prefix and not at the rest:

| `ToolCommandName` | pinned locally | installed globally |
| ----------------- | -------------- | ------------------ |
| `dotnet-killmutants` | `dotnet killmutants` | `dotnet killmutants` |
| `killmutants` | `dotnet killmutants` | `killmutants` |

The prefix would give one invocation that holds in both modes — the choice Stryker.NET made
with `dotnet-stryker`. It was declined because it buys that uniformity with the bare
`killmutants` a global install allows, which is the form the maintainer wants to type. No
name removes the `dotnet` in front of a pinned tool; the name only decides whether the
global form keeps it too.

Pinning stays the form to use in CI. Scores move with the tool's version, so it is pinned
per repository through `.config/dotnet-tools.json` — exactly how this organisation already
pins `dotnet-stryker` with `rollForward: false`, so CI and a maintainer's machine measure the
same thing. The documentation shows both invocations, and a CI script uses the pinned one.

A short command (`km`) was declined too: short pays for a tool typed bare all day, and a
pinned mutation tool is typed after `dotnet` in CI, where `dotnet km` saves little and reads
poorly.

## Cutting a release

1. Land the change on `main`, and wait for the `ci` run its push starts. A pull request runs
   only the smoke tier of the tests; that run is the full one, on all three platforms. The
   release refuses any commit that is not an ancestor of `main`, so a tag cannot bypass
   branch protection, and any commit without a successful full `ci` run of its own, so a tag
   cannot bypass the tests that run after the merge either. If that run was lost, dispatch
   `ci` on `main` and tag the commit it ran on.
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

**A partly published version is burned.** A train with several packages pushes them one after
another, so a failure in the middle leaves some published and some not. Do not retry that
version: `--skip-duplicate` would keep the packages already out — built from the earlier commit —
and publish the rest from the new one, permanently mixing two builds under one version number
with nothing left to say so. Release the next version instead. The release refuses the neighbouring
mistake on its own (a tag that points at another commit is rejected before anything is published),
but a version half-pushed under *no* tag is not something the pipeline can see; this is the one
recovery that stays a judgement call, so it is written down rather than guarded.

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
gh attestation verify KillMutants.1.2.3.nupkg --repo Reefact/kill-mutants
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
