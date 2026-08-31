using KillMutants.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    private readonly CSharpCompilation _compilation;
    private readonly CSharpParseOptions _parseOptions;
    private readonly EmitOptions _emitOptions;
    private readonly IReadOnlyList<ResourceDescription> _manifestResources;

    private ProjectCompilation(
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        EmitOptions emitOptions,
        IReadOnlyList<ResourceDescription> manifestResources)
    {
        _compilation = compilation;
        _parseOptions = parseOptions;
        _emitOptions = emitOptions;
        _manifestResources = manifestResources;
    }

    /// <summary>The syntax trees the project is built from, generated sources included.</summary>
    public IEnumerable<SyntaxTree> SyntaxTrees => _compilation.SyntaxTrees;

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

        var compilation = CSharpCompilation.Create(
            assemblyName: arguments.CompilationName ?? Path.GetFileNameWithoutExtension(arguments.OutputFileName),
            syntaxTrees: syntaxTrees,
            references: ResolveReferences(arguments, projectDirectory),
            options: NeutraliseWarningsAsErrors(arguments.CompilationOptions));

        return new ProjectCompilation(
            compilation,
            parseOptions,
            arguments.EmitOptions,
            arguments.ManifestResources);
    }

    /// <summary>Emits the compilation exactly as it stands, with no mutation applied.</summary>
    public EmitOutcome EmitBaseline() => Emit(_compilation);

    /// <summary>Emits the compilation with one mutant's change applied.</summary>
    public EmitOutcome EmitWith(Mutations.Mutant mutant)
    {
        ArgumentNullException.ThrowIfNull(mutant);

        SyntaxTree original = mutant.SyntaxTree;
        SyntaxNode mutatedRoot = original.GetRoot().ReplaceNode(mutant.OriginalNode, mutant.MutatedNode);
        SyntaxTree mutated = original.WithRootAndOptions(mutatedRoot, _parseOptions);

        return Emit(_compilation.ReplaceSyntaxTree(original, mutated));
    }

    private EmitOutcome Emit(CSharpCompilation compilation)
    {
        using var assembly = new MemoryStream();

        EmitResult result = compilation.Emit(
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

            return EmitOutcome.Failed(diagnostics);
        }

        return EmitOutcome.Succeeded(assembly.ToArray());
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
    /// Clears any project-wide <c>/warnaserror+</c>. A mutation can legitimately make previously
    /// live code unreachable (CS0162) or a variable unused; under warnings-as-errors that mutant
    /// would be reported as a compile error rather than tested, which quietly understates the score.
    /// </summary>
    private static CSharpCompilationOptions NeutraliseWarningsAsErrors(CSharpCompilationOptions options) =>
        options.WithGeneralDiagnosticOption(ReportDiagnostic.Default);
}
