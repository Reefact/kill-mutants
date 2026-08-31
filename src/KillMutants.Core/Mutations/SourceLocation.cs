using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>Where a mutation sits in the source, in the one-based terms a developer reads.</summary>
/// <param name="FilePath">Absolute path of the source file.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Character">One-based character position within the line.</param>
public sealed record SourceLocation(string FilePath, int Line, int Character)
{
    internal static SourceLocation From(SyntaxNode node)
    {
        FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);

        return new SourceLocation(
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    /// <summary>Renders as <c>file(line,character)</c>, the form editors and compilers use.</summary>
    public override string ToString()
    {
        string file = Path.GetFileName(FilePath);
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        return $"{file}({Line.ToString(culture)},{Character.ToString(culture)})";
    }
}
