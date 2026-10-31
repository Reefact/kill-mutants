using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A part of the run that cannot be measured is set aside with its reason, and the rest is measured
/// as usual (DEC0022). Before, each of these stopped the whole run: on Reefact/first-class-errors a
/// red baseline 35 minutes in took four judged projects' report with it, and a probe project outside
/// the solution stopped discovery in 15 seconds (#44).
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class SetAsideTests
{
    private static readonly MutatorName[] Families = [MutatorName.Create("Comparison")];

    /// <summary>
    /// <c>Core</c> is measured by <c>Core.Tests</c> and <c>Domain.Tests</c>; <c>Domain</c> by
    /// <c>Domain.Tests</c> alone. A test that fails in <c>Core.Tests</c> makes <c>Core</c>'s baseline
    /// red and says nothing about <c>Domain</c>'s, which is verified on its own.
    /// </summary>
    [Fact]
    public async Task A_target_with_a_red_baseline_is_set_aside_and_the_others_are_measured()
    {
        using var fixture = FixtureCopy.CreateMultiProject();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "Core.Tests", "RedOnPurpose.cs"),
            """
            namespace Core.Tests;

            public class RedOnPurpose
            {
                [Fact]
                public void Fails() => Assert.Fail("Red on purpose: Core's baseline must not pass.");
            }
            """,
            TestContext.Current.CancellationToken);

        // Comparison has sites in Core's Money and Arithmetic in Basket's `unitPrice * quantity`:
        // each target needs a mutant, or it pays no baseline at all and nothing is set aside.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison"), MutatorName.Create("Arithmetic")],
            cancellationToken: TestContext.Current.CancellationToken);

        SetAside core = Assert.Single(report.SetAside);
        Assert.Equal(SetAsideStage.Baseline, core.Stage);
        Assert.Equal("Core", core.Name);
        Assert.Contains("does not pass against unmutated code", core.Reason, StringComparison.Ordinal);
        Assert.Contains("RedOnPurpose.Fails", core.Reason, StringComparison.Ordinal);

        // Domain was measured, and only Domain.
        Assert.NotEmpty(report.Results);
        Assert.All(report.Results, result =>
            Assert.Equal("Basket.cs", Path.GetFileName(result.Mutant.Location.FilePath)));

        // A score over the part measured, which the report labels; and no gate passes.
        Assert.True(report.HasScore);
        Assert.False(report.MeasuredEverything);
    }

    /// <summary>
    /// A project MSBuild cannot read is set aside at discovery, and the rest of the directory is
    /// measured. Nothing is known of what it holds - it may have been the only suite reaching some
    /// code - so the run gives no score.
    /// </summary>
    [Fact]
    public async Task An_unreadable_project_is_set_aside_and_withholds_the_score()
    {
        using var fixture = FixtureCopy.Create();
        string probe = Path.Combine(fixture.Root, "tools", "probe");
        Directory.CreateDirectory(probe);
        await File.WriteAllTextAsync(
            Path.Combine(probe, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">", TestContext.Current.CancellationToken);

        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root, mutators: Families, cancellationToken: TestContext.Current.CancellationToken);

        SetAside unreadable = Assert.Single(report.SetAside);
        Assert.Equal(SetAsideStage.Discovery, unreadable.Stage);
        Assert.Equal("tools/probe/Probe.csproj", unreadable.Name);
        Assert.Contains("--exclude \"tools/probe/*\"", unreadable.Reason, StringComparison.Ordinal);

        Assert.NotEmpty(report.Results);
        Assert.False(report.HasScore);
    }
}
