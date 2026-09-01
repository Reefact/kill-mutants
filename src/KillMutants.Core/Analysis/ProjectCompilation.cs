using KillMutants.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace KillMutants.Analysis;

/// <summary>
/// A project's compilation, ready to be emitted with or without a mutation applied.
/// </summary>
/// <remarks>
/// Built once per project and reused for every mutant. Constructing it costs roughly 1.6 seconds,
/// almost entirely spent loading metadata references; re-emitting from it costs about 6 milliseconds.
/// Rebuilding it per mutant would invalidate the reasoning in ADR-0002.
/// </remarks>
internal sealed class ProjectCompilation
{
    private readonly CSharpCompilation _sourceCompilation;
    private readonly Compilation _generatedCompilation;
    private readonly CSharpParseOptions _parseOptions;
    private readonly EmitOptions _emitOptions;
    private readonly IReadOnlyList<ResourceDescription> _manifestResources;

    private readonly SourceGenerators _generators;
    private readonly CompilerAnalyzerConfig _analyzerConfig;
    private readonly IReadOnlyList<AdditionalText> _additionalTexts;

    private ProjectCompilation(
        CSharpCompilation sourceCompilation,
        Compilation generatedCompilation,
        CSharpParseOptions parseOptions,
        EmitOptions emitOptions,
        IReadOnlyList<ResourceDescription> manifestResources,
        SourceGenerators generators,
        CompilerAnalyzerConfig analyzerConfig,
        IReadOnlyList<AdditionalText> additionalTexts)
    {
        _sourceCompilation = sourceCompilation;
        _generatedCompilation = generatedCompilation;
        _parseOptions = parseOptions;
        _emitOptions = emitOptions;
        _manifestResources = manifestResources;
        _generators = generators;
        _analyzerConfig = analyzerConfig;
        _additionalTexts = additionalTexts;
    }

    /// <summary>
    /// The compilation mutant generation reads, generator output included.
    /// </summary>
    /// <remarks>
    /// The generated trees must be present here even though they are never mutated: a mutator asks
    /// the semantic model whether its replacement would compile, and a model that cannot see
    /// generated types answers wrongly. They are excluded from mutation by their file paths, in
    /// <see cref="Mutations.MutantGenerator"/>.
    /// </remarks>
    public Compilation Compilation => _generatedCompilation;

    /// <summary>Builds the compilation from a parsed <c>csc</c> command line.</summary>
    public static ProjectCompilation Create(CSharpCommandLineArguments arguments, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Documentation diagnostics are pure noise for mutation testing, and generating XML docs
        // for a mutant is wasted work.
        CSharpParseOptions parseOptions = arguments.ParseOptions.WithDocumentationMode(DocumentationMode.None);

        List<SyntaxTree> syntaxTrees = [];

        foreach (CommandLineSourceFile sourceFile in arguments.SourceFiles)
        {
            SourceText text = ReadSource(sourceFile.Path);

            syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, sourceFile.Path));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: arguments.CompilationName ?? Path.GetFileNameWithoutExtension(arguments.OutputFileName),
            syntaxTrees: syntaxTrees,
            references: ResolveReferences(arguments, projectDirectory),
            options: RelaxWarningsAsErrors(arguments.CompilationOptions));

        // MSBuild names the generators on the command line but not the code they contribute, so
        // they have to be run here or the compilation is missing whatever they produce.
        SourceGenerators generators = SourceGenerators.LoadFrom(arguments);
        CompilerAnalyzerConfig analyzerConfig = CompilerAnalyzerConfig.LoadFrom(arguments.AnalyzerConfigPaths);
        AdditionalText[] additionalTexts =
            [.. arguments.AdditionalFiles.Select(file => new CompilerAdditionalText(file.Path))];

        GeneratedCompilation generated = generators.Run(
            compilation, parseOptions, analyzerConfig, additionalTexts);

        // Fatal here, and only here. Everything the run goes on to measure is compared against this
        // compilation, so reconstructing it from generators that did not all run would make the
        // whole report a description of something the project never builds.
        if (generated.Failure is not null)
        {
            throw new ProjectAnalysisException(generated.Failure);
        }

        return new ProjectCompilation(
            compilation,
            generated.Compilation,
            parseOptions,
            arguments.EmitOptions,
            arguments.ManifestResources,
            generators,
            analyzerConfig,
            additionalTexts);
    }

    /// <summary>Emits the compilation exactly as it stands, with no mutation applied.</summary>
    public EmitOutcome EmitBaseline() => Emit(_sourceCompilation);

    /// <summary>
    /// Emits the compilation with every mutation site wrapped in a call that records having been
    /// reached, for the coverage pass.
    /// </summary>
    /// <remarks>
    /// The recorder returns its argument, so wrapping cannot change what an expression evaluates to
    /// or when. That is why this instrumentation needs none of the machinery a mutation switch
    /// would: there is no branch to place, so no context in which the placement is illegal, and
    /// therefore no compile-and-roll-back loop.
    /// </remarks>
    public EmitOutcome EmitInstrumented(Coverage.MutationSites sites)
    {
        ArgumentNullException.ThrowIfNull(sites);

        CSharpCompilation instrumented = _sourceCompilation;

        foreach (IGrouping<SyntaxTree, KeyValuePair<SyntaxNode, int>> inTree in
                 sites.IdentifierByNode.GroupBy(site => site.Key.SyntaxTree))
        {
            SyntaxNode root = Coverage.CoverageRewriter.Instrument(
                inTree.Key.GetRoot(), inTree.ToDictionary(site => site.Key, site => site.Value));

            instrumented = instrumented.ReplaceSyntaxTree(
                inTree.Key, inTree.Key.WithRootAndOptions(root, _parseOptions));
        }

        return Emit(instrumented.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(Coverage.CoverageProbe.Source, _parseOptions, "KillMutantsCoverageProbe.g.cs")));
    }

    /// <summary>Emits the compilation with one mutant's change applied.</summary>
    public EmitOutcome EmitWith(Mutations.Mutant mutant)
    {
        ArgumentNullException.ThrowIfNull(mutant);

        SyntaxTree original = mutant.SyntaxTree;
        SyntaxNode mutatedRoot = original.GetRoot().ReplaceNode(mutant.OriginalNode, mutant.MutatedNode);
        SyntaxTree mutated = original.WithRootAndOptions(mutatedRoot, _parseOptions);

        return Emit(_sourceCompilation.ReplaceSyntaxTree(original, mutated));
    }

    /// <summary>
    /// Emits from the source-only compilation, re-running the generators over it first.
    /// </summary>
    /// <remarks>
    /// Generator output can depend on the code being mutated, so it is regenerated for each mutant
    /// rather than reused. Measured on a project with seven generators: the first driver run costs
    /// about a second, every later one 1.4 ms, against 60 ms to emit and roughly 600 ms to run the
    /// tests. Correctness here is essentially free, so there is no reason to approximate.
    /// </remarks>
    private EmitOutcome Emit(CSharpCompilation compilation)
    {
        GeneratedCompilation generated = _generators.Run(
            compilation, _parseOptions, _analyzerConfig, _additionalTexts);

        // Not fatal for a mutant: a mutation can genuinely break what a generator reads - a changed
        // string literal an attribute depends on, say. What must not happen is judging it anyway, so
        // it is reported as a mutant that could not be built, which the score leaves out.
        if (generated.Failure is not null)
        {
            return EmitOutcome.Failed(generated.Failure);
        }

        using var assembly = new MemoryStream();

        EmitResult result = generated.Compilation.Emit(
            assembly,
            manifestResources: _manifestResources,
            options: _emitOptions);

        if (!result.Success)
        {
            string diagnostics = string.Join(
                Environment.NewLine,
                result.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Take(10)
                    .Select(diagnostic => diagnostic.ToString()));

            return EmitOutcome.Failed(diagnostics + DescribeGenerators());
        }

        return EmitOutcome.Succeeded(assembly.ToArray());
    }

    /// <summary>
    /// Adds context to a failed emit when generators are involved, so that a missing partial
    /// implementation reads as "this generator did not contribute" rather than as an unexplained
    /// defect in KillMutants.
    /// </summary>
    private string DescribeGenerators()
    {
        if (_generators.Unloadable.Count > 0)
        {
            return Environment.NewLine + Environment.NewLine +
                   "KillMutants could not load these analyzer assemblies, so any code they generate " +
                   "is missing from the compilation. This usually means the project targets a newer " +
                   $"Roslyn than KillMutants runs on ({typeof(CSharpCompilation).Assembly.GetName().Version}):" +
                   Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", _generators.Unloadable);
        }

        if (_generators.IsEmpty)
        {
            return string.Empty;
        }

        return Environment.NewLine + Environment.NewLine +
               $"{_generators.Generators.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
               "source generator(s) ran for this project. If the errors above name a missing partial " +
               "implementation, one of them did not contribute what the build expects.";
    }

    private static SourceText ReadSource(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return SourceText.From(stream, canBeEmbedded: false);
    }

    private static List<MetadataReference> ResolveReferences(
        CSharpCommandLineArguments arguments,
        string projectDirectory)
    {
        List<MetadataReference> references = [];

        foreach (CommandLineReference reference in arguments.MetadataReferences)
        {
            string path = Path.IsPathRooted(reference.Reference)
                ? reference.Reference
                : Path.Combine(projectDirectory, reference.Reference);

            references.Add(MetadataReference.CreateFromFile(path, reference.Properties));
        }

        return references;
    }

    /// <summary>
    /// Stops warnings from failing a mutant's compilation. A mutation can legitimately make live
    /// code unreachable (CS0162) or a variable unused (CS0219); under warnings-as-errors such a
    /// mutant would be recorded as a compile error instead of being tested, quietly understating
    /// the score.
    /// </summary>
    /// <remarks>
    /// Clearing the general option is not enough, which is the trap here.
    /// <c>WithGeneralDiagnosticOption</c> leaves <c>SpecificDiagnosticOptions</c> untouched, so a
    /// project built with <c>/warnaserror+:CS0162,CS0219</c> still maps both to
    /// <see cref="ReportDiagnostic.Error"/> afterwards - verified. Every diagnostic explicitly
    /// escalated to an error is therefore demoted back to a warning. Suppressions are left alone:
    /// the user silenced those deliberately and honouring that cannot cost us a mutant.
    /// </remarks>
    internal static CSharpCompilationOptions RelaxWarningsAsErrors(CSharpCompilationOptions options)
    {
        Dictionary<string, ReportDiagnostic> escalated = options.SpecificDiagnosticOptions
            .Where(option => option.Value == ReportDiagnostic.Error)
            .ToDictionary(option => option.Key, _ => ReportDiagnostic.Warn, StringComparer.Ordinal);

        CSharpCompilationOptions relaxed = options.WithGeneralDiagnosticOption(ReportDiagnostic.Default);

        return escalated.Count == 0
            ? relaxed
            : relaxed.WithSpecificDiagnosticOptions(
                options.SpecificDiagnosticOptions.SetItems(escalated));
    }
}
