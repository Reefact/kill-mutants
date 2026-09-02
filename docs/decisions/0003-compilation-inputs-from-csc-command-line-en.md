# DEC0003 | Take compilation inputs from MSBuild's `csc` command line

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-08-31 | Accepted | | |

## Context

To emit a faithful mutated assembly, KillMutants needs the project's exact compiler inputs: source
files (including SDK-generated ones), metadata references, preprocessor symbols, language version,
nullable context, unsafe and overflow settings, output kind, assembly name, and embedded resources.

Getting any of these wrong does not produce an error. It produces a *false kill*: the tests fail for
reasons unrelated to the mutation, and every mutant is reported `Killed`.

The options considered were `MSBuildWorkspace`, the MSBuild APIs with `MSBuildLocator`, Buildalyzer
(what Stryker.NET uses), and asking MSBuild directly.

Stryker.NET runs a Buildalyzer design-time build and then hand-reconstructs Roslyn's
`CSharpCompilationOptions` and `CSharpParseOptions` from raw MSBuild property strings. Our study
found this to be the largest source of accidental complexity in that codebase, dragging in
Mono.Cecil for resource recovery, a hand-rolled analyzer-config options provider, and a custom
analyzer assembly loader.

MSBuild can be asked for the `csc` command line it was about to run, and Roslyn can parse that line
itself. Asked on the fixture, it returns 205 arguments, yielding 4 source files (including the
generated `GlobalUsings.g.cs` and `AssemblyInfo.cs`), 167 metadata references,
`LanguageVersion.CSharp14` and `NullableContextOptions.Enable`, with **zero** parse errors and
nothing reconstructed by hand.

`-getItem:` and `ProvideCommandLineArgs` are MSBuild features rather than a documented public API
contract. The same information is also recoverable from a binary log.

If MSBuild considers the project up to date it skips `CoreCompile` and returns an **empty** argument
list, and `CSharpCommandLineParser` then returns a default compilation with no sources and no
references.

The command line names source generators under `/analyzer:` but does not list the code they
contribute, because the compiler produces it during the build.

## Decision

In this context, we ask MSBuild for the actual `csc` command line and let Roslyn parse it, rather
than reconstructing the compilation inputs ourselves.

## Rationale

Nothing is guessed. Every setting is the one `csc` was actually going to be given, including the
ones nobody thinks to reconstruct until a user reports a bug — which is the failure mode that
matters here, since a wrong input surfaces as a false kill rather than as an error.

The mechanism is a build invocation and a parse, and no more:

```
dotnet build <project> -t:Build \
  -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true \
  -getItem:CscCommandLineArgs
```

then `CSharpCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory: null)`. The
fixture's 205 arguments parse with zero errors, which is what makes hand reconstruction — the
largest source of accidental complexity found in Stryker.NET — unnecessary rather than merely
undesirable.

SDK-generated sources come along automatically. Omitting them is a known cause of false kills:
without the generated `AssemblyInfo.cs` the assembly version becomes `0.0.0.0` and the test host
fails to load it, which surfaces as an ordinary test failure.

Depending on MSBuild features rather than a documented contract is acceptable because the failure is
recoverable and cannot be silent: the same information can be read from a binary log, and DEC0005's
baseline check catches the breakage immediately.

## Alternatives considered

### Alternative 1 — Buildalyzer, as Stryker.NET uses it

* **Description:** run a Buildalyzer design-time build and hand-reconstruct Roslyn's
  `CSharpCompilationOptions` and `CSharpParseOptions` from raw MSBuild property strings.
* **Why rejected:** our study of Stryker.NET found this to be the largest source of accidental
  complexity in that codebase, dragging in Mono.Cecil for resource recovery, a hand-rolled
  analyzer-config options provider and a custom analyzer assembly loader — all of it work to
  reconstruct what the command line already states.

### Alternative 2 — Load the project through the MSBuild object model

* **Description:** use `MSBuildWorkspace`, or the MSBuild APIs with `MSBuildLocator`, to obtain the
  compilation.
* **Why rejected:** no reason is recorded beyond the outcome the decision claims — the chosen path
  takes no third-party dependency and no MSBuild API dependency at all.

## Consequences

### Positive

* No Buildalyzer, no `MSBuildWorkspace`, no `MSBuildLocator`, no third-party dependency at all.
* Nothing is guessed. Every setting is the one `csc` was actually going to be given.
* SDK-generated sources come along automatically, removing a known cause of false kills.

### Negative

* We depend on `-getItem:` and `ProvideCommandLineArgs`, which are MSBuild features rather than a
  documented public API contract. This is accepted.
* Source generators are the exception, and this decision originally overstated the point. The
  command line names generators under `/analyzer:` but does not list the code they contribute.
  Running them is a separate step, described in RB-002 of the robustness backlog. The command line
  still supplies everything that step needs — the generator assemblies, the analyzer config files
  and the additional files — so the decision holds; it simply does not do the whole job on its own.

### Risks

* If MSBuild considers the project up to date it skips `CoreCompile` and returns an empty argument
  list. `CSharpCommandLineParser` then cheerfully returns a default compilation with no sources and
  no references — a state that looks like a successful parse.

### Follow-up actions

* Assert the parsed result rather than presuming it: it must be non-empty and must contain `/out:`
  and `/target:` before it is used.
