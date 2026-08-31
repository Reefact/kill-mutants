using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>
/// One mutation, with an identity, ready to be compiled and tested.
/// </summary>
/// <remarks>
/// The Roslyn nodes are deliberately not part of the public surface: reporting needs the location
/// and the before/after text, and nothing outside the engine needs the syntax tree.
/// </remarks>
public sealed class Mutant
{
    internal Mutant(MutantId id, MutationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Id = id;
        Mutator = candidate.Mutator;
        OriginalNode = candidate.Original;
        MutatedNode = candidate.Replacement;
        Location = SourceLocation.From(candidate.Original);
        OriginalText = candidate.Original.ToString();
        MutatedText = candidate.Replacement.ToString();
    }

    /// <summary>Identifies this mutant within the run.</summary>
    public MutantId Id { get; }

    /// <summary>The rule that produced this mutation.</summary>
    public MutatorName Mutator { get; }

    /// <summary>Where the mutated code sits in the source.</summary>
    public SourceLocation Location { get; }

    /// <summary>The original expression, as written.</summary>
    public string OriginalText { get; }

    /// <summary>The expression this mutant puts in its place.</summary>
    public string MutatedText { get; }

    internal SyntaxNode OriginalNode { get; }

    internal SyntaxNode MutatedNode { get; }

    /// <summary>The syntax tree this mutant belongs to.</summary>
    internal SyntaxTree SyntaxTree => OriginalNode.SyntaxTree;

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Location} {OriginalText} -> {MutatedText}";
}
