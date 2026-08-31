# ADR-0004 — Run tests by launching the test project's executable

**Status:** accepted · **Date:** 2026-08-31

## Context

An xUnit 4 test project builds to an executable. Investigating how that executable reaches
Microsoft Testing Platform produced the finding that drove this decision. The entry point generated
by `xunit.v3.mtp-v2` is, in substance:

```csharp
if (args.Any(a => a == "--server" || a == "--internal-msbuild-node"))
    // Microsoft Testing Platform host
else
    // xUnit in-process console runner
```

So the MTP host is reachable **only** through JSON-RPC server mode (`--server --client-port N`,
which throws without a port) or through MSBuild (`dotnet test`). Simply running the executable uses
xUnit's own in-process console runner.

Three options, all measured:

| Option | Cost per run | Exit code on failure |
|---|---|---|
| Launch the executable directly | **~0.6 s** | 1 |
| `dotnet test --no-build` | ~1.5 s | 2 |
| `--server --client-port N` (JSON-RPC) | not measured | protocol |

The project owner was asked to arbitrate what "Microsoft Testing Platform 2 only" means, and chose:
the *projects under test* must be xUnit 4 / MTP 2 projects; KillMutants itself need not speak the
MTP protocol.

## Decision

**Launch the test project's executable as a child process.** Do not use `dotnet test`. Do not
implement an MTP JSON-RPC client.

Read the outcome from the **structured result file** (`-result-xml`), not from the exit code alone.

## Consequences

- The fastest option, by 2.5x over `dotnet test`, on the operation that dominates total run time.
- No dependency on any xUnit or MTP package. The coupling is a command-line contract, confined to
  `KillMutants.Testing.XUnit`.
- `dotnet test` and `dotnet build` must never run after a mutant is injected: both copy the pristine
  assembly back over it. Launching the executable directly is what makes injection stable.
- The runner options we will need later already exist: `-stopOnFail` (a mutant is killed by the
  first failing test), `-list tests /json` (test discovery, M4), `-id <uid>` (running one test case,
  for test-to-mutant mapping in M5).
- We give up MTP's richer per-test event stream. If M5 shows that test-to-mutant mapping genuinely
  needs it, `ITestRunner` is the seam where a server-mode implementation would be added — a
  deliberately thin seam, not a plugin system.

## Why not the exit code alone

The xUnit console runner exits **0** when a filter matches zero tests. A tool that trusted the exit
code would report such a mutant as `Survived`. The result XML carries `total`, `passed`, `failed`,
`errors` and `skipped`, so the outcome is read from counts and a run that executed no tests is
recognised as an error rather than a survival.
