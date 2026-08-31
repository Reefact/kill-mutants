using KillMutants.Projects;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.Core.Tests.Testing;

public class XUnitTestRunnerTests
{
    /// <summary>
    /// Regression test. A host that dies without writing a result file used to throw and abort the
    /// entire session. A mutation can genuinely cause that - a removed recursion base case gives an
    /// uncatchable StackOverflowException - so it has to come back as an outcome the caller can
    /// classify, not as an exception that loses every other mutant's result.
    /// </summary>
    [Fact]
    public async Task A_host_that_writes_no_result_file_is_reported_rather_than_thrown()
    {
        // A class library is not a test application: running it produces no result file.
        string library = typeof(MutationTesting).Assembly.Location;
        var project = new TestProject(library, library, Path.GetDirectoryName(library)!);

        TestRunOutcome outcome = await new XUnitTestRunner().RunAsync(
            project,
            TimeSpan.FromMinutes(1),
            stopOnFirstFailure: false,
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Crashed);
        Assert.NotNull(outcome.CrashDetail);
        Assert.False(outcome.AllPassed);
    }
}
