using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KillMutants.Analysis;

/// <summary>
/// Runs the project's source generators so that the code they contribute is part of the
/// compilation KillMutants mutates.
/// </summary>
/// <remarks>
/// MSBuild lists generators on the compiler command line under <c>/analyzer:</c> but does not list
/// their output among the sources, because the compiler produces it during the build. Without this
/// step, any project using <c>[GeneratedRegex]</c>, <c>[JsonSerializable]</c>, <c>[LibraryImport]</c>,
/// Mapperly, Refit or ASP.NET Core minimal APIs fails to compile with errors such as CS9248, and the
/// failure looks like a defect in KillMutants rather than a missing step.
/// </remarks>
internal sealed class SourceGenerators
{
    private SourceGenerators(
        IReadOnlyList<ISourceGenerator> generators,
        IReadOnlyList<string> unloadable)
    {
        Generators = generators;
        Unloadable = unloadable;
    }

    /// <summary>The generators found on the command line.</summary>
    public IReadOnlyList<ISourceGenerator> Generators { get; }

    /// <summary>Analyzer assemblies that could not be inspected, by file name.</summary>
    /// <remarks>
    /// Almost always a project pinning a newer Roslyn than the one KillMutants runs on. Recorded so
    /// that the failure can be reported by name instead of surfacing as an unexplained compile error.
    /// </remarks>
    public IReadOnlyList<string> Unloadable { get; }

    /// <summary>True when there is nothing to run.</summary>
    public bool IsEmpty => Generators.Count == 0;

    /// <summary>Loads the generators named by the compiler command line.</summary>
    public static SourceGenerators LoadFrom(CSharpCommandLineArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        List<ISourceGenerator> generators = [];
        List<string> unloadable = [];
        var loader = new AnalyzerLoader();

        foreach (CommandLineAnalyzerReference reference in arguments.AnalyzerReferences)
        {
            string path = reference.FilePath;

            try
            {
                generators.AddRange(new AnalyzerFileReference(path, loader).GetGenerators(LanguageNames.CSharp));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An analyzer built against a newer compiler cannot be inspected by this one.
                // That is a fact about the project, not a failure of the run.
                unloadable.Add(Path.GetFileName(path));
            }
        }

        return new SourceGenerators(generators, unloadable);
    }

    /// <summary>
    /// Returns <paramref name="compilation"/> with every generator's output added.
    /// </summary>
    public Compilation Run(
        Compilation compilation,
        CSharpParseOptions parseOptions,
        AnalyzerConfigOptionsProvider optionsProvider,
        IEnumerable<AdditionalText> additionalTexts)
    {
        if (IsEmpty)
        {
            return compilation;
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            Generators,
            additionalTexts,
            parseOptions,
            optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        return updated;
    }

    /// <summary>Loads analyzer assemblies into the running process.</summary>
    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
            // Dependencies resolve from the analyzer's own directory, which the default context
            // already probes. Nothing to record.
        }

        public Assembly LoadFromPath(string fullPath) =>
            AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
