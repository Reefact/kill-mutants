using System.Globalization;
using System.Xml.Linq;
using KillMutants.Processes;
using KillMutants.Projects;

namespace KillMutants.Testing.XUnit;

/// <summary>
/// Runs an xUnit 4 test project by launching its test application.
/// </summary>
/// <remarks>
/// <para>
/// This is the only type in KillMutants that knows anything about xUnit or Microsoft Testing
/// Platform, and it knows it as a command-line contract rather than as a package reference:
/// KillMutants depends on no xUnit or MTP assembly at all.
/// </para>
/// <para>
/// The test application is launched directly rather than through <c>dotnet test</c>. That is both
/// faster and necessary: <c>dotnet test</c> and <c>dotnet build</c> run MSBuild, which copies the
/// pristine assembly back over an injected mutant. See ADR-0004.
/// </para>
/// </remarks>
internal sealed class XUnitTestRunner : ITestRunner
{
    /// <inheritdoc />
    public async Task<TestRunOutcome> RunAsync(
        TestProject testProject,
        TimeSpan timeout,
        bool stopOnFirstFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testProject);

        string resultPath = Path.Combine(
            Path.GetTempPath(),
            $"killmutants-{Guid.NewGuid():N}.xml");

        List<string> arguments = [testProject.AssemblyPath, "-noLogo", "-noColor", "-result-xml", resultPath];

        if (stopOnFirstFailure)
        {
            arguments.Add("-stopOnFail");
        }

        try
        {
            ProcessResult process = await ProcessRunner.RunAsync(
                    "dotnet",
                    arguments,
                    testProject.OutputDirectory,
                    timeout,
                    // Keep the runner quiet and deterministic: no CI reporter should latch on to
                    // an environment variable and change the output we parse.
                    environment: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1",
                        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (process.TimedOut)
            {
                return TestRunOutcome.FromTimeout(process.Duration);
            }

            return ReadOutcome(resultPath, process);
        }
        finally
        {
            DeleteQuietly(resultPath);
        }
    }

    /// <summary>
    /// Reads the counts from the structured result file rather than trusting the exit code, which
    /// cannot distinguish "everything passed" from "nothing ran".
    /// </summary>
    private static TestRunOutcome ReadOutcome(string resultPath, ProcessResult process)
    {
        if (!File.Exists(resultPath))
        {
            throw new TestExecutionException(
                $"The test application produced no result file." +
                $"{Environment.NewLine}{process.CombinedOutput}");
        }

        XElement? assembly = XDocument.Load(resultPath).Root?.Element("assembly");

        if (assembly is null)
        {
            throw new TestExecutionException($"The test application's result file names no assembly.");
        }

        return new TestRunOutcome(
            Total: ReadCount(assembly, "total"),
            Failed: ReadCount(assembly, "failed"),
            Errors: ReadCount(assembly, "errors"),
            Duration: process.Duration,
            TimedOut: false);
    }

    private static int ReadCount(XElement assembly, string attributeName)
    {
        string? value = assembly.Attribute(attributeName)?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a run over.
        }
    }
}
