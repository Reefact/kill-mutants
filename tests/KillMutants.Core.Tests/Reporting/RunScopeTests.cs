using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A partial report whose out-of-scope mutants are simply absent is indistinguishable from a full run
/// that happened to have that many mutants. The scope is what tells them apart.
/// </summary>
public class RunScopeTests
{
    [Fact]
    public void The_whole_codebase_is_not_a_partial_run()
    {
        Assert.False(RunScope.WholeCodebase.IsPartial);
        Assert.Equal("the whole codebase", RunScope.WholeCodebase.ToString());
    }

    [Fact]
    public void A_scope_that_names_an_earlier_state_is_a_partial_run()
    {
        var scope = new RunScope("0123456789abcdef", "fedcba9876543210", true, 3);

        Assert.True(scope.IsPartial);
        Assert.Equal("changes from 0123456789ab to fedcba987654", scope.ToString());
    }

    /// <summary>
    /// A run that measures something no state names describes what it built, and the report has to
    /// say so - otherwise it reads as a run anyone could reproduce from the two names.
    /// </summary>
    [Fact]
    public void Code_that_no_state_names_exactly_is_reported_as_edited()
    {
        var scope = new RunScope("0123456789abcdef", "fedcba9876543210", false, 3);

        Assert.Equal(
            "changes from 0123456789ab to edited code based on fedcba987654",
            scope.ToString());
    }

    /// <summary>
    /// The cut is a display width, not knowledge about what a name looks like. A state named by
    /// something other than a hash - a build number, a label a job chose - comes through whole.
    /// </summary>
    [Fact]
    public void A_state_named_shorter_than_the_display_width_is_not_cut()
    {
        var scope = new RunScope("build-41", "build-42", true, 1);

        Assert.Equal("changes from build-41 to build-42", scope.ToString());
    }
}
