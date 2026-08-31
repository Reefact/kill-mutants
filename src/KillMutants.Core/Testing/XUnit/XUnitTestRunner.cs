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
/// <para>
/// Every run passes <c>-automated</c>, which is what makes the launch work on any xUnit 4 project
/// rather than only on default ones. The generated entry point comes in two shapes, and the
/// property <c>UseMicrosoftTestingPlatformRunner</c> flips between them:
/// </para>
/// <code>
/// // default                                   // UseMicrosoftTestingPlatformRunner=true
/// if (--server || --internal-msbuild-node)     if (-automated || @@)
///     MTP host;                                    xUnit console runner;
/// else                                         else
///     xUnit console runner;                        MTP host;
/// </code>
/// <para>
/// <c>-automated</c> selects the xUnit console runner under both shapes. Without it, a project
/// using the second shape sends our arguments to the Microsoft Testing Platform host, which
/// rejects them with "Unknown option", exits 5 and writes no result file - verified.
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

        List<string> arguments =
        [
            testProject.AssemblyPath,
            // Must come first and must always be present: it is what pins the run to the xUnit
            // console runner whichever entry point the project generated. See the remarks above.
            "-automated",
            "-noLogo",
            "-noColor",
            "-result-xml",
            resultPath,
        ];

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
            // No result file. Either the host died mid-run - which a mutation can genuinely cause -
            // or it refused to start. Report it rather than throw: deciding which of those it is
            // belongs to the caller, who knows whether this was the baseline or a mutant.
            return TestRunOutcome.FromCrash(
                process.Duration,
                $"The test application exited with code " +
                $"{process.ExitCode.ToString(CultureInfo.InvariantCulture)} without writing a result file." +
                $"{Environment.NewLine}{Truncate(process.CombinedOutput)}");
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

    private static string Truncate(string output) =>
        output.Length <= 2000 ? output : output[..2000] + "...";

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
