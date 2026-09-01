using System.Collections.Immutable;
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

    /// <summary>Wraps generators supplied directly, for tests that need one that misbehaves.</summary>
    internal static SourceGenerators Of(IReadOnlyList<ISourceGenerator> generators) =>
        new(generators, []);

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
    /// Returns <paramref name="compilation"/> with every generator's output added, and says whether
    /// every generator actually ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half of that sentence is the point. Roslyn does not fail a build when a generator
    /// throws: it reports <c>CS8784</c> or <c>CS8785</c> as a <em>warning</em>, drops that
    /// generator's contribution, and carries on. Measured against Roslyn 5.9 with a generator that
    /// throws from its initialiser: <c>CS8784/Warning</c>, and a compilation that still emits.
    /// </para>
    /// <para>
    /// For a build that is what you want - the compiler errors that follow point at the real
    /// problem. Here it is the difference between measuring the project and measuring something
    /// else: if the code the generator should have produced is not what the selected tests exercise,
    /// the assembly emits, the tests pass, and every verdict that follows describes an assembly that
    /// is not the one the build produces. The failure has to be carried out of here, because only
    /// the caller knows whether it is fatal - reconstructing the baseline - or a mutant that cannot
    /// be judged.
    /// </para>
    /// </remarks>
    public GeneratedCompilation Run(
        Compilation compilation,
        CSharpParseOptions parseOptions,
        AnalyzerConfigOptionsProvider optionsProvider,
        IEnumerable<AdditionalText> additionalTexts)
    {
        if (IsEmpty)
        {
            return new GeneratedCompilation(compilation, Failure: null);
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            Generators,
            additionalTexts,
            parseOptions,
            optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return new GeneratedCompilation(updated, Describe(diagnostics));
    }

    /// <summary>
    /// The generators that did not run, or null when they all did.
    /// </summary>
    /// <remarks>
    /// An error from a generator counts too. The project built before KillMutants touched it, so a
    /// generator reporting an error against this compilation means the compilation is not the one
    /// the build compiled.
    /// </remarks>
    private static string? Describe(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] failures = [.. diagnostics.Where(IsFailure)];

        if (failures.Length == 0)
        {
            return null;
        }

        return
            "A source generator did not run, so the compilation is missing code the build has:" +
            Environment.NewLine + "  " +
            string.Join(
                Environment.NewLine + "  ",
                failures.Take(10).Select(diagnostic => diagnostic.ToString()));
    }

    /// <summary>
    /// <c>CS8784</c> is a generator that failed to initialise, <c>CS8785</c> one that failed while
    /// generating. Both are warnings, and both mean output is missing.
    /// </summary>
    private static bool IsFailure(Diagnostic diagnostic) =>
        diagnostic.Severity == DiagnosticSeverity.Error ||
        diagnostic.Id is "CS8784" or "CS8785";

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
