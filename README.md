# KillMutants

> A modern, opinionated mutation testing tool for .NET, built for xUnit 4.

KillMutants changes your code in small, deliberate ways — turning `>=` into `>`, `&&` into `||` —
and then runs your tests. A test suite that still passes against changed code is a test suite that
was not checking that code. Each change is a *mutant*; a mutant your tests catch is **killed**, one
they miss **survives**.

## Status

Early. Milestone 1 is a deliberately tiny vertical slice: one project pair, one mutator, one mutant,
executed end to end for real. The intent is a foundation that has been shown to work, rather than a
large catalog resting on an engine that has never run.

## Scope

KillMutants is narrow on purpose, and the narrowness is what keeps it simple.

**Supported:** xUnit 4, modern .NET, C#, SDK-style projects.

**Not supported:** xUnit 2 and earlier, NUnit, MSTest, TUnit, VSTest, .NET Framework, non-SDK
projects, `packages.config`, F#, Visual Basic. No abstraction exists in anticipation of them.

"xUnit 4" is the `xunit.v3` package family at version `4.0.0`; its Microsoft Testing Platform 2
flavour is `xunit.v3.mtp-v2`. Projects built on Microsoft Testing Platform 2 are part of the
ecosystem KillMutants targets, and it runs them — but speaking the MTP protocol is not a goal in
itself, and no MTP coupling is introduced without a concrete need that xUnit 4 cannot already meet.

## Documentation

Every document is maintained in English and in French. Files named `LICENSE` and `README.md` keep
their conventional names and are English only.

| | English | Français |
|---|---|---|
| Architecture — the pipeline, the domain model, the risks | [architecture-en.md](docs/architecture-en.md) | [architecture-fr.md](docs/architecture-fr.md) |
| Study of Stryker.NET — what we learned, and what we deliberately did differently | [stryker-net-en.md](docs/study/stryker-net-en.md) | [stryker-net-fr.md](docs/study/stryker-net-fr.md) |
| Robustness backlog — edge cases inherited from Stryker.NET as specifications and tests | [robustness-backlog-en.md](docs/robustness-backlog-en.md) | [robustness-backlog-fr.md](docs/robustness-backlog-fr.md) |

### Architecture decisions

The few choices that are expensive to reverse. Each is recorded in [docs/adr](docs/adr).

| | English | Français |
|---|---|---|
| ADR-0001 — Record architecture decisions | [en](docs/adr/0001-record-architecture-decisions-en.md) | [fr](docs/adr/0001-record-architecture-decisions-fr.md) |
| ADR-0002 — One compilation per mutant | [en](docs/adr/0002-one-compilation-per-mutant-en.md) | [fr](docs/adr/0002-one-compilation-per-mutant-fr.md) |
| ADR-0003 — Compilation inputs from the csc command line | [en](docs/adr/0003-compilation-inputs-from-csc-command-line-en.md) | [fr](docs/adr/0003-compilation-inputs-from-csc-command-line-fr.md) |
| ADR-0004 — Run tests by launching the test executable | [en](docs/adr/0004-run-tests-by-launching-the-test-executable-en.md) | [fr](docs/adr/0004-run-tests-by-launching-the-test-executable-fr.md) |
| ADR-0005 — Verify the baseline before mutating | [en](docs/adr/0005-verify-the-baseline-before-mutating-en.md) | [fr](docs/adr/0005-verify-the-baseline-before-mutating-fr.md) |

## Licence

[PolyForm Internal Use License 1.0.0](LICENSE).
