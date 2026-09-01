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
    internal Mutant(MutantId id, MutationCandidate candidate, string root)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(root);

        Id = id;
        Mutator = candidate.Mutator;
        OriginalNode = candidate.Original;
        MutatedNode = candidate.Replacement;
        Location = SourceLocation.From(candidate.Original);
        OriginalText = candidate.Original.ToString();
        MutatedText = candidate.Replacement.ToString();
        RelativePath = Relative(Location.FilePath, root);
        Key = MutantKey.For(
            RelativePath, Location.Line, Location.Character, Mutator, OriginalText, MutatedText);
    }

    /// <summary>Numbers this mutant within the run, for progress lines and logs.</summary>
    /// <remarks>
    /// A counter, and only useful inside one run. <see cref="Key"/> is what survives between runs.
    /// </remarks>
    public MutantId Id { get; }

    /// <summary>Identifies this mutant by its content, the same in every run - see <see cref="MutantKey"/>.</summary>
    public MutantKey Key { get; }

    /// <summary>The source file, relative to the directory the run was pointed at.</summary>
    public string RelativePath { get; }

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

    /// <summary>
    /// The path relative to the run's root, or the absolute path when it lies outside it.
    /// </summary>
    /// <remarks>
    /// Falling back to the absolute path keeps a linked file identifiable rather than turning it
    /// into a chain of <c>..</c> segments that says nothing.
    /// </remarks>
    private static string Relative(string path, string root)
    {
        if (root.Length == 0 || path.Length == 0)
        {
            return path;
        }

        string relative = Path.GetRelativePath(root, path);

        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}
