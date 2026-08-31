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

**Supported:** modern .NET, C#, xUnit 4, Microsoft Testing Platform 2, SDK-style projects.

**Not supported:** xUnit 2 and earlier, NUnit, MSTest, TUnit, VSTest, .NET Framework, non-SDK
projects, `packages.config`, F#, Visual Basic. No abstraction exists in anticipation of them.

"xUnit 4" is the `xunit.v3` package family at version `4.0.0`; its Microsoft Testing Platform 2
flavour is `xunit.v3.mtp-v2`.

## Documentation

- [Architecture](docs/architecture.md) — the pipeline, the domain model, and the risks
- [Study of Stryker.NET](docs/study/stryker-net.md) — what we learned, and what we deliberately
  did differently
- [Architecture decisions](docs/adr) — the few choices that are expensive to reverse

## Licence

[PolyForm Internal Use License 1.0.0](LICENSE).
