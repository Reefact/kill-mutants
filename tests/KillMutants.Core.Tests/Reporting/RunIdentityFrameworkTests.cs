using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A framework is a build, and verdicts reached on one say nothing about another, so a series cannot
/// be continued across two. See DEC0021 and DEC0015.
/// </summary>
public class RunIdentityFrameworkTests
{
    private static readonly CodeState Code = new("0123456789abcdef", IsExact: true, Directory: string.Empty);

    private static RunIdentity Identity(string? framework) =>
        new(Code, "1.0.0", "Release", Coverage: true, [], [], [], TargetFramework: framework);

    [Fact]
    public void A_series_cannot_be_continued_on_another_framework()
    {
        string? why = Identity("net10.0").WhyNotContinuedBy(Identity("net8.0"));

        Assert.NotNull(why);
        Assert.Contains("net8.0", why, StringComparison.Ordinal);
        Assert.Contains("net10.0", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_names_a_framework_cannot_continue_one_that_did_not()
    {
        Assert.NotNull(Identity("net10.0").WhyNotContinuedBy(Identity(framework: null)));
        Assert.NotNull(Identity(framework: null).WhyNotContinuedBy(Identity("net10.0")));
    }

    [Fact]
    public void The_same_framework_in_another_case_is_the_same_framework()
    {
        Assert.Null(Identity("NET10.0").WhyNotContinuedBy(Identity("net10.0")));
    }

    [Fact]
    public void Two_runs_that_named_none_continue_each_other_as_before()
    {
        Assert.Null(Identity(framework: null).WhyNotContinuedBy(Identity(framework: null)));
    }
}
