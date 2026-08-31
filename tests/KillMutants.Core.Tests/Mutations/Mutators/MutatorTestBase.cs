using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>Shared plumbing for exercising one mutator against a snippet of real, compiled C#.</summary>
internal static class MutatorTestBase
{
    /// <summary>
    /// Runs <paramref name="mutator"/> over every node of an expression, with the semantic model of
    /// a real compilation. Declaring the operands means the mutators' binding checks see real types.
    /// </summary>
    public static IReadOnlyList<MutationCandidate> MutateAll(IMutator mutator, string expression) =>
        MutateAll(mutator, "bool a, bool b, int x, int y", $"object M({{0}}) => {expression};");

    /// <summary>Runs <paramref name="mutator"/> over a member declared with arbitrary parameters.</summary>
    public static IReadOnlyList<MutationCandidate> MutateAll(
        IMutator mutator, string parameters, string memberTemplate)
    {
        string member = memberTemplate.Contains("{0}", StringComparison.Ordinal)
            ? memberTemplate.Replace("{0}", parameters, StringComparison.Ordinal)
            : memberTemplate;
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            $"public struct Money {{ public static Money operator +(Money l, Money r) => l; }} " +
            $"public class C {{ public {member} }}");

        return [.. tree.GetRoot().DescendantNodes().SelectMany(node => mutator.Mutate(node, model))];
    }

    /// <summary>The rewritten source produced by each candidate, for readable assertions.</summary>
    public static IReadOnlyList<string> MutatedTexts(IMutator mutator, string expression) =>
        [.. MutateAll(mutator, expression).Select(candidate => candidate.Replacement.ToString())];

    /// <summary>The rewritten source for a member declared with arbitrary parameters.</summary>
    public static IReadOnlyList<string> MutatedTexts(
        IMutator mutator, string parameters, string memberTemplate) =>
        [.. MutateAll(mutator, parameters, memberTemplate).Select(c => c.Replacement.ToString())];
}
