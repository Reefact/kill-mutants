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
    public static Compilation From(string source, string path = "/src/Sample.cs")
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);

        return CSharpCompilation.Create(
            "Sample",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>Compiles <paramref name="source"/> and returns its single tree's semantic model.</summary>
    public static (SyntaxTree Tree, SemanticModel Model) WithModel(string source)
    {
        Compilation compilation = From(source);
        SyntaxTree tree = compilation.SyntaxTrees.Single();

        return (tree, compilation.GetSemanticModel(tree));
    }
}
