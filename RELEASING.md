# Releasing

A release is a **tag push**. Nothing is published from a developer's machine, and no
step of the publish path lives outside this repository.

## Release trains

The engine and the tool version independently. Each owns a tag prefix; the
authoritative mapping is [`tools/trains.sh`](tools/trains.sh).

| Train | Tag           | Publishes                   |
| ----- | ------------- | --------------------------- |
| `lib` | `lib-v1.2.3`  | the KillMutants engine      |
| `cli` | `cli-v1.2.3`  | the kill-mutants .NET tool  |

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

1. **`NUGET_USER` repository variable** → the nuget.org account username (the profile
   name, not the email). A *variable*, not a secret: it is an identifier, not a
   credential, and masking it would only make a failed login harder to read.
2. **A trusted-publishing policy on nuget.org** for each published package id, bound to
   owner `Reefact`, repository `kill-mutants`, workflow `release.yml`. No long-lived
   API key is stored anywhere.

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
gh attestation verify kill-mutants.1.2.3.nupkg --repo Reefact/kill-mutants
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
