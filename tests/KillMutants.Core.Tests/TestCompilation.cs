using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests;

/// <summary>
/// Builds a real compilation from a snippet, so mutator tests exercise the same semantic analysis
/// the tool performs rather than a stand-in for it.
/// </summary>
internal static class TestCompilation
{
    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    /// <summary>Compiles <paramref name="source"/> as a library.</summary>
    /// <remarks>
    /// <para>
    /// <c>Deterministic</c> is set explicitly because <see cref="CSharpCompilationOptions"/> defaults
    /// it to <see langword="false"/>, and one guarantee depends on it entirely: comparing two emitted
    /// assemblies is only meaningful when the same compilation emits the same bytes twice. Measured
    /// against Roslyn 5.9 - without it, two emits of an identical program differ, because the module
    /// version id and the header timestamp are freshly generated each time.
    /// </para>
    /// <para>
    /// The real pipeline is not affected: the compiler command line MSBuild reports carries
    /// <c>/deterministic+</c>, so <c>ProjectCompilation</c> has always compared like with like. This
    /// is about the snippets these tests compile themselves.
    /// </para>
    /// </remarks>
    public static Compilation From(string source, string path = "/src/Sample.cs")
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);

        return CSharpCompilation.Create(
            "Sample",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithDeterministic(true));
    }

    /// <summary>Compiles <paramref name="source"/> and returns its single tree's semantic model.</summary>
    public static (SyntaxTree Tree, SemanticModel Model) WithModel(string source)
    {
        Compilation compilation = From(source);
        SyntaxTree tree = compilation.SyntaxTrees.Single();

        return (tree, compilation.GetSemanticModel(tree));
    }
}
