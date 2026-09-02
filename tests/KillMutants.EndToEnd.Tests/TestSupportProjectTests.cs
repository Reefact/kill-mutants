using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A library of builders and assertions beside the tests is scaffolding, not the subject. Getting
/// that wrong costs in both directions, and this fixture shows each.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tests -&gt; Support -&gt; Library</c>, where <c>Support</c> is a class library referencing
/// <c>xunit.v3.assert</c>. That package is not a choice: xUnit v3 refuses to be referenced by a
/// non-executable project - "xUnit.net v3 test projects must be executable" - and points such a
/// library at <c>xunit.v3.assert</c> or <c>xunit.v3.extensibility.core</c> instead.
/// </para>
/// <para>
/// Both begin with <c>xunit.v3</c>, so recognising a test project by that prefix alone called this
/// library one. The run then tried to launch it and stopped: "could not find 'xunit.v3.core.dll'".
/// Following xUnit's own instruction made a project impossible to measure.
/// </para>
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class TestSupportProjectTests
{
    /// <summary>
    /// The regression for the detection fix. Before it, this run did not report a low score - it did
    /// not run at all.
    /// </summary>
    [Fact]
    public async Task Production_code_behind_a_test_support_library_is_still_reached()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(report.Results, result => FileOf(result) == "Money.cs");
    }

    /// <summary>
    /// Undeclared, the support library is indistinguishable from the code under test, so it is
    /// mutated - and its untested scaffolding drags the score down with findings nobody set out to
    /// measure. Measured on this fixture: 4 mutants and 25 %, against 2 and 50 % once declared.
    /// </summary>
    [Fact]
    public async Task An_undeclared_support_library_is_mutated_like_any_other_project()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(report.Results, result => FileOf(result) == "Affordability.cs");
    }

    /// <summary>
    /// Declared, it is left alone - and the library behind it is still found, which is the other
    /// half of the contract. A declaration that hid the code under test would cost more than it saves.
    /// </summary>
    [Fact]
    public async Task A_declared_support_library_is_skipped_without_hiding_what_it_references()
    {
        using var fixture = FixtureCopy.CreateTestSupportProject();
        fixture.DeclareTheSupportProject();

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        string[] mutated = [.. report.Results
            .Select(FileOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["Money.cs"], mutated);
    }

    private static string FileOf(MutantResult result) =>
        Path.GetFileName(result.Mutant.Location.FilePath);
}
