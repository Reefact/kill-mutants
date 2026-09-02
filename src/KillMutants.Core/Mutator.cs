using KillMutants.Operators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants;

/// <summary>Finds every mutant a C# source file admits.</summary>
/// <remarks>
/// Finding is deliberately separate from running. What a file admits is a property of the file, so
/// it is answered by parsing alone — no build, no test host, no project system. Whether a mutant
/// then survives is a property of the test suite, and belongs to a different stage that consumes
/// this one's output.
/// </remarks>
public sealed class Mutator
{
    private readonly IReadOnlyList<IMutationOperator> _operators;

    /// <summary>Creates a mutator using every operator the engine ships.</summary>
    public Mutator() : this(DefaultOperators()) { }

    /// <summary>Creates a mutator using the given operators, in order.</summary>
    public Mutator(IReadOnlyList<IMutationOperator> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);
        _operators = operators;
    }

    /// <summary>The operators enabled when none are named.</summary>
    public static IReadOnlyList<IMutationOperator> DefaultOperators() =>
    [
        new BinaryOperatorMutator(),
        new BooleanLiteralMutator(),
    ];

    /// <summary>Every mutant <paramref name="source"/> admits, in the order they appear in the file.</summary>
    public IReadOnlyList<Mutant> FindMutants(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source);
        var text = tree.GetText();
        var mutants = new List<Mutant>();

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            foreach (var op in _operators)
            {
                foreach (var mutation in op.Mutate(node))
                {
                    // LinePosition is 0-based; reports and editors are 1-based.
                    var start = text.Lines.GetLinePosition(mutation.Span.Start);
                    mutants.Add(new Mutant(
                        mutation.Operator,
                        mutation.Original,
                        mutation.Replacement,
                        mutation.Span,
                        start.Line + 1,
                        start.Character + 1));
                }
            }
        }

        // DescendantNodes is a pre-order walk, so mutants come out in the order a reader meets them
        // — except where one node yields several, which stay grouped. Sorting by position makes the
        // sequence match the file exactly, which is what a report and a diff both want.
        mutants.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return mutants;
    }
}
