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
            options: RelaxWarningsAsErrors(arguments.CompilationOptions));

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
