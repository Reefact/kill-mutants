using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>
/// A change a mutator proposes: replace <paramref name="Original"/> with <paramref name="Replacement"/>.
/// Internal because it carries Roslyn nodes; it becomes a <see cref="Mutant"/> once it has an identity.
/// </summary>
internal sealed record MutationCandidate(MutatorName Mutator, SyntaxNode Original, SyntaxNode Replacement);
