using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using KillMutants.Testing.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A mutant that cannot be built was never testable, and the score is defined over the mutants that
/// were. Counting one as undetected holds the number down with a verdict nobody could have reached.
/// </summary>
/// <remarks>
/// The run used to decide "nothing covers this" before trying to build it, so an unbuildable mutant
/// in uncovered code was reported as NoCoverage - which the score counts as undetected - instead of
/// CompileError, which it leaves out. Emitting first costs one compilation per uncovered mutant and
/// no test run at all, which is where the time actually goes.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class UnbuildableMutantTests
{
    [Fact]
    public async Task A_mutant_that_cannot_be_built_is_untestable_even_where_no_test_reaches_it()
    {
        using var fixture = FixtureCopy.Create();

        fixture.AddUncoveredCode();

        MutationTestReport report = await new MutationTestSession(
                new XUnitTestRunner(),
                "Release",
                timeoutPolicy: null,
                workerCount: 1,
                measureCoverage: true,
                exclude: null,
                catalog: MutatorCatalog.Of([new BreaksTheBuild()]),
                verifyKills: 0)
            .RunAsync(fixture.Root, TestContext.Current.CancellationToken);

        MutantResult mutant = Assert.Single(report.Results);

        // Uncovered - the method it sits in is called by nothing - and unbuildable. The second fact
        // is the one that decides, because it is the one that makes the mutant unjudgeable.
        Assert.Equal(MutantStatus.CompileError, mutant.Status);
        Assert.Equal(MutantOutcome.Untestable, mutant.Outcome);
        Assert.Contains("CS0103", mutant.Detail!, StringComparison.Ordinal);

        // And it is outside the score rather than counted against it.
        Assert.Equal(0, report.Detected);
        Assert.Equal(0, report.Undetected);
        Assert.Equal(1, report.Untestable);
    }

    /// <summary>
    /// Replaces one specific literal with a name that does not exist.
    /// </summary>
    /// <remarks>
    /// Every shipped mutator refuses a replacement that would not compile, which is the right
    /// behaviour and makes an unbuildable mutant hard to come by on purpose. This one exists to
    /// produce exactly one, at a site the fixture puts in code no test calls.
    /// </remarks>
    private sealed class BreaksTheBuild : IMutator
    {
        public MutatorName Name { get; } = MutatorName.Create("BreaksTheBuild");

        public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node is LiteralExpressionSyntax literal &&
                literal.Token.ValueText == "4242")
            {
                yield return new MutationCandidate(
                    Name,
                    literal,
                    SyntaxFactory.IdentifierName("ThisNameDoesNotExist"));
            }
        }
    }
}
