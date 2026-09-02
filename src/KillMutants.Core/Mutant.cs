using Microsoft.CodeAnalysis.Text;

namespace KillMutants;

/// <summary>A single mutation the engine can apply to a source file.</summary>
/// <param name="Operator">The operator that produced it, e.g. <c>arithmetic</c>.</param>
/// <param name="Original">The source text being replaced, as written.</param>
/// <param name="Replacement">The text that replaces it.</param>
/// <param name="Span">Where in the file, in characters.</param>
/// <param name="Line">1-based line of <see cref="Span"/>, for reporting.</param>
/// <param name="Column">1-based column of <see cref="Span"/>, for reporting.</param>
public sealed record Mutant(
    string Operator,
    string Original,
    string Replacement,
    TextSpan Span,
    int Line,
    int Column)
{
    /// <summary>Applies this mutant to <paramref name="source"/> and returns the mutated text.</summary>
    /// <remarks>
    /// A mutant is a span and a replacement, so applying one is a substring operation and nothing
    /// more. Keeping it that way is deliberate: the engine never has to re-serialise a syntax tree,
    /// so a mutated file differs from the original in exactly the characters this mutant names.
    /// </remarks>
    public string ApplyTo(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return string.Concat(
            source.AsSpan(0, Span.Start),
            Replacement,
            source.AsSpan(Span.End));
    }

    /// <summary>A one-line description, e.g. <c>arithmetic 3:12 '+' -> '-'</c>.</summary>
    public override string ToString() => $"{Operator} {Line}:{Column} '{Original}' -> '{Replacement}'";
}
