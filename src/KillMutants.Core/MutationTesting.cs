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
    /// <param name="workerCount">
    /// How many mutants to test at once. Defaults to half the logical processors, because each
    /// worker starts a test host that runs the suite's own tests in parallel too.
    /// </param>
    /// <param name="measureCoverage">
    /// Measure which tests reach which mutants, and run only those. Turning this off runs the whole
    /// suite for every mutant, which is slower but needs no instrumented build.
    /// </param>
    /// <param name="progress">Told where the run has got to, so a caller can show it.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <exception cref="Projects.ProjectAnalysisException">The projects could not be analysed.</exception>
    /// <exception cref="BaselineVerificationException">The unmutated code does not pass its tests.</exception>
    /// <exception cref="Testing.TestExecutionException">The test application could not be run.</exception>
    public static Task<MutationTestReport> RunAsync(
        string searchDirectory,
        string configuration = "Release",
        int? workerCount = null,
        bool measureCoverage = true,
        IProgress<Reporting.MutationTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new MutationTestSession(
            new XUnitTestRunner(), configuration, timeoutPolicy: null, workerCount, measureCoverage, progress);

        return session.RunAsync(searchDirectory, cancellationToken);
    }
}
