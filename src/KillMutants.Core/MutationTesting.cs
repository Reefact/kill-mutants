using KillMutants.Execution;
using KillMutants.Reporting;
using KillMutants.Testing.XUnit;

namespace KillMutants;

/// <summary>Runs mutation testing over a directory containing an xUnit 4 test project.</summary>
public static class MutationTesting
{
    /// <summary>
    /// Discovers the test project and the project it exercises, mutates the latter, and reports
    /// which mutations the tests caught.
    /// </summary>
    /// <param name="searchDirectory">The directory to search for projects.</param>
    /// <param name="configuration">The build configuration to analyse and run.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <exception cref="Projects.ProjectAnalysisException">The projects could not be analysed.</exception>
    /// <exception cref="BaselineVerificationException">The unmutated code does not pass its tests.</exception>
    /// <exception cref="Testing.TestExecutionException">The test application could not be run.</exception>
    public static Task<MutationTestReport> RunAsync(
        string searchDirectory,
        string configuration = "Release",
        CancellationToken cancellationToken = default)
    {
        var session = new MutationTestSession(new XUnitTestRunner(), configuration);

        return session.RunAsync(searchDirectory, cancellationToken);
    }
}
