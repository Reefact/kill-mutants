using System.Diagnostics;
using KillMutants.Processes;

namespace KillMutants.Core.Tests.Processes;

public class ProcessRunnerTests
{
    /// <summary>
    /// The mechanism the Timeout status rests on. A mutation can turn a terminating loop into an
    /// endless one, and a run that never comes back would otherwise hang the whole session.
    /// </summary>
    [Fact]
    public async Task A_process_that_never_finishes_is_killed_and_reported_as_timed_out()
    {
        (string command, string[] arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "timeout /t 60 /nobreak"])
            : ("/bin/sh", new[] { "-c", "sleep 60" });

        var stopwatch = Stopwatch.StartNew();

        ProcessResult result = await ProcessRunner.RunAsync(
            command,
            arguments,
            Path.GetTempPath(),
            TimeSpan.FromSeconds(2),
            cancellationToken: TestContext.Current.CancellationToken);

        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);

        // The point is that it came back at all: without the kill this would run for a minute.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"The run took {stopwatch.Elapsed} - the process was not killed.");
    }

    [Fact]
    public async Task A_process_that_finishes_in_time_reports_its_exit_code()
    {
        (string command, string[] arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "exit 3"])
            : ("/bin/sh", new[] { "-c", "exit 3" });

        ProcessResult result = await ProcessRunner.RunAsync(
            command,
            arguments,
            Path.GetTempPath(),
            TimeSpan.FromSeconds(30),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
    }
}
