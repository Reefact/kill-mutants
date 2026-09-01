using System.Globalization;
using System.Text.Json;
using System.Xml;
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
    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, string> QuietEnvironment = new(StringComparer.Ordinal)
    {
        // Keep the runner quiet and deterministic: no CI reporter should latch on to an environment
        // variable and change the output we parse.
        ["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestName>> DiscoverAsync(
        TestProject testProject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testProject);

        ProcessResult result = await RunProcessAsync(
                testProject,
                [testProject.AssemblyPath, "-automated", "-noLogo", "-list", "tests/json"],
                DiscoveryBudget,
                environment: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.TimedOut)
        {
            throw new TestExecutionException($"Listing the tests in '{testProject.Name}' timed out.");
        }

        try
        {
            return [.. JsonSerializer.Deserialize<string[]>(result.StandardOutput)!
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(TestName.Create)];
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            throw new TestExecutionException(
                $"Could not read the list of tests in '{testProject.Name}'." +
                $"{Environment.NewLine}{result.CombinedOutput}",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<TestRunOutcome> RunAsync(
        TestRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string resultPath = Path.Combine(Path.GetTempPath(), $"killmutants-{Guid.NewGuid():N}.xml");

        List<string> arguments =
        [
            request.TestProject.AssemblyPath,
            // Must come first and must always be present: it is what pins the run to the xUnit
            // console runner whichever entry point the project generated. See the remarks above.
            "-automated",
            "-noLogo",
            "-noColor",
            "-result-xml",
            resultPath,
        ];

        if (request.StopOnFirstFailure)
        {
            arguments.Add("-stopOnFail");
        }

        foreach (TestName testName in request.TestNames ?? [])
        {
            // Repeating -method is a union, so this selects exactly the named tests.
            arguments.Add("-method");
            arguments.Add(testName.ToString());
        }

        try
        {
            ProcessResult process = await RunProcessAsync(
                    request.TestProject, arguments, request.Timeout, request.Environment, cancellationToken)
                .ConfigureAwait(false);

            return process.TimedOut
                ? TestRunOutcome.FromTimeout(process.Duration)
                : ReadOutcome(resultPath, process);
        }
        finally
        {
            Scratch.DeleteFile(resultPath);
        }
    }

    private static Task<ProcessResult> RunProcessAsync(
        TestProject testProject,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> merged = new(QuietEnvironment, StringComparer.Ordinal);

        foreach ((string key, string value) in environment ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            merged[key] = value;
        }

        return ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            testProject.OutputDirectory,
            timeout,
            merged,
            cancellationToken);
    }

    /// <summary>
    /// Reads the counts from the structured result file rather than trusting the exit code, which
    /// cannot distinguish "everything passed" from "nothing ran".
    /// </summary>
    /// <remarks>
    /// Internal rather than private so a half-written file can be handed to it directly. Producing
    /// one through a real process means killing a test host at the exact moment it is writing, which
    /// is not something a test can ask for reliably.
    /// </remarks>
    internal static TestRunOutcome ReadOutcome(string resultPath, ProcessResult process)
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

        XElement? assembly;

        try
        {
            assembly = XDocument.Load(resultPath).Root?.Element("assembly");
        }
        catch (XmlException exception)
        {
            // A file that stops mid-element is a host that was killed while writing it, which is the
            // same event as writing no file at all - and a mutation is one of the things that kills a
            // host. Throwing here ended the whole session over one mutant.
            return Unreadable(process, $"stops part way through: {exception.Message}");
        }

        if (assembly is null)
        {
            // Well-formed and still unusable. Same answer, for the same reason: whatever the host
            // wrote, it is not a result, and one mutant must not take every other verdict with it.
            return Unreadable(process, "names no assembly");
        }

        return new TestRunOutcome(
            Total: ReadCount(assembly, "total"),
            Failed: ReadCount(assembly, "failed"),
            Errors: ReadCount(assembly, "errors"),
            Duration: process.Duration,
            TimedOut: false,
            FailedTests: ReadFailures(assembly));
    }

    /// <summary>Reports a result file that cannot be believed as a host that did not finish.</summary>
    private static TestRunOutcome Unreadable(ProcessResult process, string why) =>
        TestRunOutcome.FromCrash(
            process.Duration,
            $"The test application exited with code " +
            $"{process.ExitCode.ToString(CultureInfo.InvariantCulture)} and its result file {why}." +
            $"{Environment.NewLine}{Truncate(process.CombinedOutput)}");

    /// <summary>Names the tests that did not pass, in the form <c>-method</c> accepts.</summary>
    /// <remarks>
    /// The runner writes a test's full case name, arguments included -
    /// <c>Class.Method(age: 18)</c> - and that form matches nothing when handed back as a filter,
    /// measured against xUnit 4. Everything up to the first parenthesis is the method, which does
    /// match; a C# method name cannot contain one, so the cut is unambiguous.
    /// </remarks>
    private static IReadOnlyList<TestName> ReadFailures(XElement assembly) =>
    [
        .. assembly
            .Descendants("test")
            .Where(test => test.Attribute("result")?.Value is not ("Pass" or "Skip"))
            .Select(test => test.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name![..IndexOfArguments(name!)].TrimEnd())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(TestName.Create),
    ];

    private static int IndexOfArguments(string name) =>
        name.IndexOf('(', StringComparison.Ordinal) is var index && index >= 0 ? index : name.Length;

    private static string Truncate(string output) =>
        output.Length <= 2000 ? output : output[..2000] + "...";

    private static int ReadCount(XElement assembly, string attributeName)
    {
        string? value = assembly.Attribute(attributeName)?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }
}
