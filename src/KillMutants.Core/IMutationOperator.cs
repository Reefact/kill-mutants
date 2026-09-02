using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KillMutants;

/// <summary>One replacement an operator proposes: where in the file, and what goes there.</summary>
/// <param name="Operator">The rule that produced it, e.g. <c>arithmetic</c>. Reported to the user.</param>
/// <param name="Span">The characters being replaced.</param>
/// <param name="Original">Those characters, as written.</param>
/// <param name="Replacement">What replaces them.</param>
/// <remarks>
/// The name travels with the mutation rather than sitting on the operator, so one class may emit
/// several families — swapping <c>+</c> and swapping <c>==</c> is the same code and different news
/// to whoever reads the report.
/// </remarks>
public readonly record struct Mutation(string Operator, TextSpan Span, string Original, string Replacement);

/// <summary>A rule that turns one syntax node into zero or more mutations.</summary>
/// <remarks>
/// Operators see one node at a time and know nothing about the rest of the file. That is what lets
/// <see cref="Mutator"/> find every mutant in a single walk, and what keeps each operator small
/// enough to be read and tested on its own.
/// </remarks>
public interface IMutationOperator
{
    /// <summary>The mutations this operator proposes for <paramref name="node"/>, possibly none.</summary>
    IEnumerable<Mutation> Mutate(SyntaxNode node);
}
