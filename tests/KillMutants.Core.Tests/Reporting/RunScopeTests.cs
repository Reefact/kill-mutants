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
    public void A_scope_with_a_base_revision_is_a_partial_run()
    {
        var scope = new RunScope("0123456789abcdef", "fedcba9876543210", false, 3);

        Assert.True(scope.IsPartial);
        Assert.Equal("changes from 01234567 to fedcba98", scope.ToString());
    }

    /// <summary>
    /// A run on a dirty tree measures the tree, not the commit, and the report has to say which.
    /// </summary>
    [Fact]
    public void A_dirty_working_tree_is_named_as_such()
    {
        var scope = new RunScope("0123456789abcdef", "fedcba9876543210", true, 3);

        Assert.Equal("changes from 01234567 to the working tree at fedcba98", scope.ToString());
    }
}
