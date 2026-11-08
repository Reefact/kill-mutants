# KillMutants

|  |  |
| :-- | :-- |
| **Build** | [![ci](https://github.com/Reefact/kill-mutants/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Reefact/kill-mutants/actions/workflows/ci.yml) |
| **Quality** | [![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=reefact_kill-mutants&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=reefact_kill-mutants) [![Coverage](https://sonarcloud.io/api/project_badges/measure?project=reefact_kill-mutants&metric=coverage)](https://sonarcloud.io/summary/new_code?id=reefact_kill-mutants) |
| **Security** | [![codeql](https://github.com/Reefact/kill-mutants/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/Reefact/kill-mutants/actions/workflows/codeql.yml) [![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/Reefact/kill-mutants/badge)](https://scorecard.dev/viewer/?uri=github.com/Reefact/kill-mutants) [![OpenSSF Best Practices](https://www.bestpractices.dev/projects/16431/badge)](https://www.bestpractices.dev/projects/16431) |
| **Package** | ![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) |
| **Project** | [![License: PolyForm Internal Use 1.0.0](https://img.shields.io/badge/License-PolyForm%20Internal%20Use%201.0.0-blue)](LICENSE) [![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-fe5196?logo=conventionalcommits&logoColor=white)](https://www.conventionalcommits.org) |

**A mutation testing tool for .NET that finds the tests which check nothing — built for xUnit 4.**

## 🚨 The problem

This code is 100 % covered, and the tests are green:

```csharp
public static decimal FeeFor(decimal orderTotal)
{
    return orderTotal >= 100m ? 0m : 4.90m;
}
```

```csharp
[Fact] public void Shipping_is_free_on_a_large_order()  => Assert.Equal(0m,    Shipping.FeeFor(150m));
[Fact] public void Shipping_is_charged_on_a_small_order() => Assert.Equal(4.90m, Shipping.FeeFor(50m));
```

Now change `>=` into `>`. The rule is wrong — an order of exactly 100 is charged — and **both tests
still pass**. Coverage said the line was executed. It never said the line was *checked*.

## ✅ The solution

KillMutants makes that change for you, and every other small change it knows — `>=` into `>`,
`&&` into `||`, `"text"` into `""` — then runs your tests against each one. A change the tests
catch is a **killed** mutant. A change they let through is a **survivor**, and it names, to the
character, the code your tests do not check:

```text
KillMutants

Survived (2)

  Shipping.cs
     7:16  orderTotal >= 100m    -> orderTotal > 100m     [Comparison]
    12:38  weightInGrams <= 2000 -> weightInGrams < 2000  [Comparison]

No coverage (5)

  Labels.cs
    7:52  "Free shipping"      -> ""   [StringLiteral]
    ...

Mutants: 13
Killed: 6
Survived: 2
No coverage: 5

Mutation score: 46.15%
```

Two boundary tests later, both survivors are dead and the score says so. That loop — run, read,
write the test the report asks for — is the whole product.

## 📦 Install

KillMutants is a `dotnet` tool. It is not on nuget.org yet, so pack it from this repository and
install from the folder you packed into:

```bash
dotnet pack src/KillMutants.Cli -c Release -o ./artifacts
dotnet tool install --global KillMutants --add-source ./artifacts --prerelease
```

Then, from the root of any repository with xUnit 4 test projects:

```bash
killmutants
```

No configuration, no attribute, no package to add to your tests. It finds the test projects, the
projects they reference, and goes.

## ⚡ On a pull request

A full sweep of a real repository takes minutes; a diff takes seconds. `--since` judges only what
the change touched, and answers with a verdict rather than a score:

```bash
killmutants --since origin/main
```

```text
Scope: changes from 24beb47cd6f2 to edited code based on 24beb47cd6f2 (1 file changed)
Verdict: 2 undetected mutant(s) in the selected scope.
```

The process exits `1`, so the gate is one line of CI. A change to a *test* widens the run to
everything that test exercised, so deleting an assertion cannot pass as a clean diff.
→ [Judging a pull request](docs/guides/pull-requests-en.md)

## 🧭 What makes it different

- **An honest score.** Code no test reaches counts as *undetected*, not as "not applicable": a
  project cannot raise its score by adding code nothing tests.
- **Trustworthy verdicts.** Every mutant gets its own compilation and its own fresh test process,
  so no static field or cache can carry over from one mutant to the next. `--verify-kills` re-runs
  a sample of the kills alone and reports any that do not repeat.
- **Verifiable findings.** Every kill names the tests that failed; every mutant carries a stable
  key, so two reports of the same commit can be joined.
- **Built for CI budgets.** `--stop-after` reports what was judged when time runs out, every
  verdict lands in a log the moment it is final, and `--resume` finishes a large project over
  several runs.
- **Narrow on purpose.** xUnit 4, modern .NET, C#, SDK-style projects. No abstraction exists for
  anything else, which is what keeps it small and fast.

## ⚙️ Configuration

A project keeps its habits in `killmutants.json`, beside its code, so the catalogue behind a score
is versioned with the code it scored:

```jsonc
{
  // Error messages here are not asserted on, so this family only adds noise.
  "without": ["StringLiteral"],
  "exclude": ["tests/fixtures/*"],
  "breakAt": 80,
  "reportJson": "artifacts/mutation.json"
}
```

Every option has a key; anything on the command line wins.
→ [Configuration reference](docs/guides/configuration-en.md)

## 📚 Documentation

**→ [Start with the ten-minute guide](docs/guides/getting-started-en.md)**

| | |
| --- | --- |
| [Documentation index](docs/README.md) | everything, organised, in English and French |
| [Core concepts](docs/guides/core-concepts-en.md) | what a mutant is, the five verdicts, how the score is computed |
| [The mutators](docs/guides/mutators-en.md) | the sixteen families, what each asks of your tests, and how to choose |
| [Reading a report](docs/guides/reading-a-report-en.md) | the console report, the JSON report, the exit codes |
| [Judging a pull request](docs/guides/pull-requests-en.md) | `--since`: a verdict on a change, in seconds |
| [Continuous integration](docs/guides/continuous-integration-en.md) | gates, deadlines, series, a ready-made GitHub Actions job |
| [Design principles](docs/guides/design-principles-en.md) | what it refuses on purpose, and why |

Maintainer material — the [architecture](docs/architecture-en.md), the
[study of Stryker.NET](docs/study/stryker-net-en.md), the
[robustness backlog](docs/robustness-backlog-en.md) and the
[decision records](docs/decisions/README.md) — is under `docs/` too.

## 🎯 Scope

**Supported:** xUnit 4 (the `xunit.v3` package family at version 4, Microsoft Testing Platform
flavour included), modern .NET, C#, SDK-style projects.

**Not supported:** xUnit 2 and earlier, NUnit, MSTest, TUnit, .NET Framework, non-SDK projects,
`packages.config`, F#, Visual Basic. No abstraction exists in anticipation of them.

> **Status.** Early, but working end to end, and run against its own source
> [every week](.github/workflows/selfcheck.yml). The command line and the JSON report are the
> contract; the console output is for people and may change between versions.

## 🤝 Contributing

Issues and pull requests are welcome. A change to what the tool does arrives with the automated test
that fails without it; that rule and the other conventions for proposing a change — commits,
history, documentation — are in [`CLAUDE.md`](CLAUDE.md). [`SECURITY.md`](SECURITY.md) says how to
report a vulnerability.

```bash
dotnet build KillMutants.slnx -c Release
dotnet test  --solution KillMutants.slnx -c Release
```

The repository targets the .NET 10 SDK (pinned in `global.json`). `.github/workflows/ci.yml` holds
the authoritative build and test invocation.

A pull request is checked in two tiers, and the required `CI` check says which one it ran:

| Tier | When | What it runs, on Linux, Windows and macOS |
| ---- | ---- | ------------------------------------------ |
| smoke | a pull request, by default | the Release build with warnings as errors, every unit and property test, and the end-to-end tests marked `[Smoke]` |
| full | every push to `main`, a manual run, a pull request labelled `full-validation`, and a pull request touching a path the smoke tier cannot vouch for | every suite, end-to-end included |

Those paths are listed in `ci.yml`, with the reason for each: the end-to-end tests and their
fixtures, the execution loop, test launch, coverage probe and process handling, what every project
is compiled with, and the workflow itself. For a risky change outside them, the author or any
reviewer adds the `full-validation` label, and the maintainer checks the tier `CI` reports before
merging.

The end-to-end suite runs whole after every merge, and an issue titled *main failed its full
validation* stays open while `main`'s head is red. A release is refused unless the tagged commit
passed the full tier (see [`RELEASING.md`](RELEASING.md)).

## 📜 Licence

[PolyForm Internal Use License 1.0.0](LICENSE) — source-available, not open source: free to read,
build, modify and use for your own or your company's internal business operations; you may not
distribute the software. See [`REEFACT_EXPLOITATION_LICENSE.md`](REEFACT_EXPLOITATION_LICENSE.md)
for how it reaches you as a published package.
