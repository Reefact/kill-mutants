using KillMutants.Processes;
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
            new TestRunRequest(project, TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Crashed);
        Assert.NotNull(outcome.CrashDetail);
        Assert.False(outcome.AllPassed);
    }

    /// <summary>
    /// The other half of the same event. A host killed while writing its result leaves a file that
    /// stops mid-element, and reading that threw an XmlException - which is neither a
    /// TestExecutionException nor an outcome, so it travelled all the way out and ended the session.
    /// One mutant took every other mutant's verdict with it.
    /// </summary>
    [Fact]
    public void A_result_file_the_host_did_not_finish_writing_is_reported_rather_than_thrown()
    {
        TestRunOutcome outcome = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies>
              <assembly total="3" failed="0" errors="0">
                <collection>
                  <test name="Sample.Library.Tests.AgesTests.Adult" result="Pa
            """);

        Assert.True(outcome.Crashed);
        Assert.Contains("stops part way through", outcome.CrashDetail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Well formed and still not a result. Same answer, and for the same reason: this used to throw
    /// too, from two lines further down.
    /// </summary>
    [Fact]
    public void A_result_file_naming_no_assembly_is_reported_rather_than_thrown()
    {
        TestRunOutcome outcome = Read("<?xml version=\"1.0\" encoding=\"utf-8\"?><assemblies />");

        Assert.True(outcome.Crashed);
        Assert.Contains("names no assembly", outcome.CrashDetail!, StringComparison.Ordinal);
    }

    /// <summary>And a result file that is one reads normally, or the two above prove nothing.</summary>
    [Fact]
    public void A_complete_result_file_is_read()
    {
        TestRunOutcome outcome = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies>
              <assembly total="2" failed="1" errors="0">
                <collection>
                  <test name="Sample.Library.Tests.AgesTests.Adult" result="Pass" />
                  <test name="Sample.Library.Tests.AgesTests.Minor(age: 17)" result="Fail" />
                </collection>
              </assembly>
            </assemblies>
            """);

        Assert.False(outcome.Crashed);
        Assert.Equal(2, outcome.Total);
        Assert.Equal(1, outcome.Failed);
        Assert.Equal(
            ["Sample.Library.Tests.AgesTests.Minor"],
            outcome.FailedTests.Select(name => name.ToString()));
    }

    private static TestRunOutcome Read(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"killmutants-test-{Guid.NewGuid():N}.xml");

        File.WriteAllText(path, content);

        try
        {
            return XUnitTestRunner.ReadOutcome(
                path, new ProcessResult(134, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A kill nobody can reproduce is not a kill, and the name in the report is what makes it
    /// reproducible. The runner writes both a label and an identity; only one of them can be handed
    /// back as a filter.
    /// </summary>
    [Fact]
    public void A_failing_test_is_named_by_its_identity_not_by_its_display_name()
    {
        TestRunOutcome outcome = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies>
              <assembly total="1" failed="1" errors="0">
                <collection>
                  <test name="a customer over eighteen is an adult"
                        type="Sample.Library.Tests.AgesTests"
                        method="An_adult_is_an_adult"
                        result="Fail" />
                </collection>
              </assembly>
            </assemblies>
            """);

        Assert.Equal(
            ["Sample.Library.Tests.AgesTests.An_adult_is_an_adult"],
            outcome.FailedTests.Select(name => name.ToString()));
    }

    /// <summary>
    /// Two cases of one theory are one test to re-run, and the identity says so directly - where the
    /// display name had to be cut at its first parenthesis to get there.
    /// </summary>
    [Fact]
    public void The_cases_of_one_theory_collapse_to_the_method()
    {
        TestRunOutcome outcome = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies>
              <assembly total="2" failed="2" errors="0">
                <collection>
                  <test name="Sample.Library.Tests.AgesTests.Minor(age: 17)"
                        type="Sample.Library.Tests.AgesTests" method="Minor" result="Fail" />
                  <test name="Sample.Library.Tests.AgesTests.Minor(age: 16)"
                        type="Sample.Library.Tests.AgesTests" method="Minor" result="Fail" />
                </collection>
              </assembly>
            </assemblies>
            """);

        Assert.Equal(
            ["Sample.Library.Tests.AgesTests.Minor"],
            outcome.FailedTests.Select(name => name.ToString()));
    }

    /// <summary>
    /// And a writer that gives only a name still gets the old treatment, rather than nothing.
    /// </summary>
    [Fact]
    public void A_result_without_an_identity_falls_back_to_its_name()
    {
        TestRunOutcome outcome = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies>
              <assembly total="1" failed="1" errors="0">
                <collection>
                  <test name="Sample.Library.Tests.AgesTests.Minor(age: 17)" result="Fail" />
                </collection>
              </assembly>
            </assemblies>
            """);

        Assert.Equal(
            ["Sample.Library.Tests.AgesTests.Minor"],
            outcome.FailedTests.Select(name => name.ToString()));
    }
}
