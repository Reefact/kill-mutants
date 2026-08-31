using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>Shared plumbing for exercising one mutator against a snippet of real C#.</summary>
internal static class MutatorTestBase
{
    /// <summary>Runs <paramref name="mutator"/> over every node of a compiled-shaped snippet.</summary>
    public static IReadOnlyList<MutationCandidate> MutateAll(IMutator mutator, string expression)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            $"class C {{ bool M(bool a, bool b, int x, int y) => {expression}; }}");

        return [.. tree.GetRoot().DescendantNodes().SelectMany(mutator.Mutate)];
    }

    /// <summary>The rewritten source produced by each candidate, for readable assertions.</summary>
    public static IReadOnlyList<string> MutatedTexts(IMutator mutator, string expression) =>
        [.. MutateAll(mutator, expression).Select(candidate => candidate.Replacement.ToString())];
}
