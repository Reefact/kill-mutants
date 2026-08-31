# ADR-0003 — Take compilation inputs from MSBuild's `csc` command line

**Status:** accepted · **Date:** 2026-08-31

## Context

To emit a faithful mutated assembly, KillMutants needs the project's exact compiler inputs: source
files (including SDK-generated ones), metadata references, preprocessor symbols, language version,
nullable context, unsafe and overflow settings, output kind, assembly name, and embedded resources.

Getting any of these wrong does not produce an error. It produces a *false kill*: the tests fail
for reasons unrelated to the mutation, and every mutant is reported `Killed`.

The options considered were `MSBuildWorkspace`, the MSBuild APIs with `MSBuildLocator`, Buildalyzer
(what Stryker.NET uses), and asking MSBuild directly.

Stryker.NET runs a Buildalyzer design-time build and then hand-reconstructs Roslyn's
`CSharpCompilationOptions` and `CSharpParseOptions` from raw MSBuild property strings. Our study
found this to be the largest source of accidental complexity in that codebase, dragging in
Mono.Cecil for resource recovery, a hand-rolled analyzer-config options provider, and a custom
analyzer assembly loader.

## Decision

Ask MSBuild for the **actual `csc` command line**, and let Roslyn parse it:

```
dotnet build <project> -t:Build \
  -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true \
  -getItem:CscCommandLineArgs
```

then `CSharpCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory: null)`.

Verified on the fixture: 205 arguments, yielding 4 source files (including the generated
`GlobalUsings.g.cs` and `AssemblyInfo.cs`), 167 metadata references, `LanguageVersion.CSharp14`,
`NullableContextOptions.Enable` — with **zero** parse errors and nothing reconstructed by hand.

## Consequences

- No Buildalyzer, no `MSBuildWorkspace`, no `MSBuildLocator`, no third-party dependency at all.
- Nothing is guessed. Every setting is the one `csc` was actually going to be given, including the
  ones nobody thinks to reconstruct until a user reports a bug.
- Generated sources come along automatically. Omitting them is a known cause of false kills:
  without the generated `AssemblyInfo.cs` the assembly version becomes `0.0.0.0` and the test host
  fails to load it, which surfaces as an ordinary test failure.
- We depend on `-getItem:` and `ProvideCommandLineArgs`, which are MSBuild features rather than a
  documented public API contract. This is accepted: the fallback, if it ever breaks, is to read the
  same information from a binary log, and the risk is caught immediately by ADR-0005's baseline check.

## Known trap

If MSBuild considers the project up to date it skips `CoreCompile` and returns an **empty**
argument list. `CSharpCommandLineParser` then cheerfully returns a default compilation with no
sources and no references. The parsed result must therefore be asserted non-empty and required to
contain `/out:` and `/target:` before it is used.
