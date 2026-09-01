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

    /// <summary>Loads analyzer assemblies, and whatever they bring with them.</summary>
    /// <remarks>
    /// <para>
    /// A generator is rarely a single file. Mapperly, Refit, protobuf and most hand-written
    /// generators ship helper assemblies beside themselves, and MSBuild lists only the generator on
    /// the compiler command line - the rest are expected to be found next to it.
    /// </para>
    /// <para>
    /// <see cref="AssemblyLoadContext.Default"/> does not look there. Measured against the .NET 10
    /// SDK with a generator whose dependency sat in the same directory: the generator loaded, then
    /// failed during initialisation with <c>FileNotFoundException</c>, and Roslyn reported that as
    /// <c>CS8784</c> - a <em>warning</em>. The generator contributed nothing, the project then failed
    /// to compile for want of the code it should have produced, and the error blamed KillMutants for
    /// a reconstruction that was in fact correct.
    /// </para>
    /// <para>
    /// So the directories of everything Roslyn registers are remembered, and anything the default
    /// context cannot find is looked for there. Hooking <c>Resolving</c> rather than loading eagerly
    /// is what keeps this safe: the event fires only after the normal search has failed, so an
    /// analyzer directory can never win over the host's own copy of <c>Microsoft.CodeAnalysis</c> or
    /// of a framework assembly, and type identity across the boundary is preserved.
    /// </para>
    /// </remarks>
    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        private static readonly HashSet<string> Directories = new(StringComparer.Ordinal);

        static AnalyzerLoader() => AssemblyLoadContext.Default.Resolving += ResolveFromAnalyzerDirectories;

        public void AddDependencyLocation(string fullPath)
        {
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
            {
                lock (Directories)
                {
                    Directories.Add(directory);
                }
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            // Roslyn registers the analyzer itself, but a generator loaded directly still needs its
            // own directory on the list for its dependencies to be found.
            AddDependencyLocation(fullPath);

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }

        private static Assembly? ResolveFromAnalyzerDirectories(AssemblyLoadContext context, AssemblyName name)
        {
            if (name.Name is not { Length: > 0 } simpleName)
            {
                return null;
            }

            string[] candidates;

            lock (Directories)
            {
                candidates = [.. Directories];
            }

            foreach (string directory in candidates)
            {
                string path = Path.Combine(directory, simpleName + ".dll");

                if (File.Exists(path))
                {
                    return context.LoadFromAssemblyPath(path);
                }
            }

            return null;
        }
    }
}
