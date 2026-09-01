using System.Diagnostics;
using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A kill nobody can reproduce is not a kill. This test takes a report at its word and settles it
/// the way a sceptical reader would: put the mutation into the file by hand, run the test the report
/// names, and watch it fail - with KillMutants nowhere in the loop.
/// </summary>
/// <remarks>
/// The reason this matters is a field report on another tool: a mutant declared killed by CI and
/// survived locally, with twenty-one test identifiers named as its killers, none of which resolved
/// to anything runnable. Settling that took days. What makes a verdict disputable is the ability to
/// re-run it, and that is a property of the <em>report</em>, not of the engine.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class ReplayableKillTests
{
    [Fact]
    public async Task A_reported_kill_can_be_reproduced_by_hand_from_the_report_alone()
    {
        using var fixture = FixtureCopy.Create();

        // One family, so the run is short. Any killed mutant will do.
        MutationTestReport report = await MutationTesting.RunAsync(
            fixture.Root,
            mutators: [MutatorName.Create("Comparison")],
            cancellationToken: TestContext.Current.CancellationToken);

        MutantResult killed = report.Results.First(
            result => result.Status == MutantStatus.Killed && result.KilledBy.Count > 0);

        // Everything from here uses only what the report says: a file, a position, two pieces of
        // text and a test name.
        string source = Path.Combine(fixture.Root, killed.Mutant.RelativePath);
        ApplyByHand(source, killed.Mutant);

        string killer = killed.KilledBy[0].ToString();

        Assert.Equal(0, await BuildAsync(fixture.Root));
        Assert.Equal(1, await RunTestAsync(fixture.Root, killer));
    }

    /// <summary>
    /// Rewrites the file exactly as the report describes, and refuses to guess.
    /// </summary>
    /// <remarks>
    /// Asserting that the original text really is at the reported position is half the point: a
    /// position that does not name what the report says it names would make every other field
    /// suspect, and a replacement done blindly would hide it.
    /// </remarks>
    private static void ApplyByHand(string path, Mutant mutant)
    {
        string[] lines = File.ReadAllLines(path);
        int line = mutant.Location.Line - 1;
        int character = mutant.Location.Character - 1;

        Assert.InRange(line, 0, lines.Length - 1);
        Assert.StartsWith(
            mutant.OriginalText,
            lines[line][character..],
            StringComparison.Ordinal);

        lines[line] = string.Concat(
            lines[line].AsSpan(0, character),
            mutant.MutatedText,
            lines[line].AsSpan(character + mutant.OriginalText.Length));

        File.WriteAllLines(path, lines);
    }

    private static Task<int> BuildAsync(string root) =>
        RunAsync("dotnet", ["build", TestProject(root), "-c", "Release", "-v:q", "--nologo"]);

    /// <summary>
    /// Runs one test by the name the report gave, through the runner's own command line.
    /// </summary>
    /// <remarks>
    /// Exit code 1 is a failing suite; 0 would mean the named test passed against the mutation, and
    /// the report's kill would be a claim about something that did not happen. `-automated` pins the
    /// console runner, which both entry-point shapes accept.
    /// </remarks>
    private static Task<int> RunTestAsync(string root, string testName) =>
        RunAsync(
            "dotnet",
            [
                Path.Combine(
                    root, "Sample.Library.Tests", "bin", "Release", "net10.0",
                    "Sample.Library.Tests.dll"),
                "-automated", "-noLogo", "-noColor", "-method", testName,
            ]);

    private static string TestProject(string root) =>
        Path.Combine(root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");

    private static async Task<int> RunAsync(string fileName, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;

        await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return process.ExitCode;
    }
}
