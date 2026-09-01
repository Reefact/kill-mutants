using KillMutants.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// A generator that throws does not fail a build: Roslyn reports it as a warning, drops what it
/// would have contributed, and carries on. That is the right call for a compiler and the wrong one
/// for a measuring tool, which would then describe an assembly the project does not build.
/// </summary>
public class SourceGeneratorFailureTests
{
    private static readonly Compilation Compilation = TestCompilation.From("class C { }");

    /// <summary>
    /// Measured rather than assumed: <c>CS8784</c>, severity <em>Warning</em>, and a compilation
    /// that still emits. The whole finding rests on that severity.
    /// </summary>
    [Fact]
    public void A_generator_that_cannot_initialise_is_reported_as_a_failure()
    {
        GeneratedCompilation result = Run(new ThrowingOnInitialisation());

        Assert.NotNull(result.Failure);
        Assert.Contains("CS8784", result.Failure, StringComparison.Ordinal);
        Assert.Contains("did not run", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generator_that_throws_while_generating_is_reported_as_a_failure()
    {
        GeneratedCompilation result = Run(new ThrowingWhileGenerating());

        Assert.NotNull(result.Failure);
        Assert.Contains("CS8785", result.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a generator doing its job says nothing, including one that reports diagnostics of its
    /// own - a generator warning about the code it reads is ordinary, and must not stop a run.
    /// </summary>
    [Fact]
    public void A_generator_that_runs_is_not_a_failure()
    {
        GeneratedCompilation result = Run(new WarningGenerator());

        Assert.Null(result.Failure);
        Assert.Contains(
            result.Compilation.SyntaxTrees,
            tree => tree.ToString().Contains("Generated", StringComparison.Ordinal));
    }

    [Fact]
    public void A_project_without_generators_has_nothing_to_fail()
    {
        GeneratedCompilation result = SourceGenerators.Of([]).Run(
            Compilation, CSharpParseOptions.Default, CompilerAnalyzerConfig.LoadFrom([]), []);

        Assert.Null(result.Failure);
        Assert.Same(Compilation, result.Compilation);
    }

    private static GeneratedCompilation Run(IIncrementalGenerator generator) =>
        SourceGenerators.Of([generator.AsSourceGenerator()]).Run(
            Compilation, CSharpParseOptions.Default, CompilerAnalyzerConfig.LoadFrom([]), []);

    private sealed class ThrowingOnInitialisation : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(
                _ => throw new InvalidOperationException("this generator is broken"));
    }

    private sealed class ThrowingWhileGenerating : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (_, _) => throw new InvalidOperationException("this generator is broken"));
    }

    private sealed class WarningGenerator : IIncrementalGenerator
    {
        // RS2008 asks for release tracking, which is for analyzers that ship. This descriptor exists
        // for the length of one test and is never published.
#pragma warning disable RS2008
        private static readonly DiagnosticDescriptor Descriptor = new(
            "GEN001", "A generator's own diagnostic", "Nothing is wrong here", "Usage",
            DiagnosticSeverity.Warning, isEnabledByDefault: true);
#pragma warning restore RS2008

        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (production, _) =>
                {
                    production.ReportDiagnostic(Diagnostic.Create(Descriptor, Location.None));
                    production.AddSource("Generated.g.cs", "class Generated { }");
                });
    }
}
