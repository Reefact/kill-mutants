using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.Core.Tests.Testing;

/// <summary>
/// Repeating <c>-method</c> selects exactly the tests that reach a mutant, which is where a run's
/// speed comes from - up to the point where the command line itself becomes the limit.
/// </summary>
/// <remarks>
/// Windows caps a process command line at 32 767 characters. A mutation site on shared utility code
/// can be reached by enough tests to pass that, and Process.Start then throws and takes the whole
/// session with it - on the most covered mutants rather than the least. Running the suite unfiltered
/// is slower and never wrong, which is the trade the run already makes when coverage is unknown.
/// </remarks>
public class FilterBudgetTests
{
    [Fact]
    public void An_ordinary_selection_is_named_test_by_test()
    {
        Assert.True(XUnitTestRunner.FitsOnACommandLine(Names(50)));
    }

    [Fact]
    public void A_selection_too_long_for_a_command_line_is_not()
    {
        Assert.False(XUnitTestRunner.FitsOnACommandLine(Names(2000)));
    }

    /// <summary>
    /// The budget leaves room for the executable, the result path and the other switches, so it sits
    /// below the limit it defends rather than at it.
    /// </summary>
    [Fact]
    public void The_budget_leaves_room_for_the_rest_of_the_command_line()
    {
        Assert.InRange(XUnitTestRunner.FilterBudget, 1, 32_767);
    }

    [Fact]
    public void Nothing_selected_always_fits()
    {
        Assert.True(XUnitTestRunner.FitsOnACommandLine([]));
    }

    private static TestName[] Names(int count) =>
        [.. Enumerable.Range(0, count).Select(index =>
            TestName.Create($"Sample.Library.Tests.SomeFixtureName.A_test_method_number_{index}"))];
}
