# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each release train keeps its entries here until its projects carry changelogs of
their own; see `tools/trains.sh` for the trains and `RELEASING.md` for the procedure.

`tools/packaging/check-changelog.sh` refuses to publish a version this file does not
document with a **dated** heading, so an entry left under "Unreleased" fails the
release rather than shipping undocumented.

## [Unreleased]

### Added

- `KillMutants.Core`: the mutation engine's first stage — given C# source, it finds
  every mutant the file admits and reports each with its operator, its position and
  the text that replaces it. Binary operators (arithmetic, relational, equality,
  logical) and boolean literals.
- Release pipeline: tag-triggered publish to NuGet with OIDC trusted publishing,
  SLSA build provenance attestation, embedded SPDX SBOM, and a side-effect-free
  rehearsal (`release-dryrun`) on every pull request.
