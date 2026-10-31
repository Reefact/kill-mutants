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

### Changed

- A part of the run that cannot be measured is set aside with its reason, and the run carries on:
  a project MSBuild cannot read, a test project that does not build, a compilation that cannot be
  reconstructed, a red baseline, a failed coverage pass. Each was a stop; now the report names them
  under "Set aside" (and `setAside` in the JSON), the score covers the part measured and says so -
  or is withheld when a project could not be read - no gate passes, and the exit code is `2`. See
  DEC0022.

### Added

- `--target-framework <tfm>` (and `targetFramework` in `killmutants.json`) names the one framework the
  test projects run on. A test project targeting several frameworks was refused outright; it is
  now refused only without the option, and the message lists what it offers. See DEC0021.
- Release pipeline: tag-triggered publish to NuGet with OIDC trusted publishing,
  SLSA build provenance attestation, embedded SPDX SBOM, and a side-effect-free
  rehearsal (`release-dryrun`) on every pull request.
