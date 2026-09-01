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

    /// <summary>
    /// Cancelling is not the same as walking away. The timeout path always killed the process; the
    /// caller's own token skipped that catch entirely, so the exception travelled out, the Process
    /// object was disposed - which stops nothing - and the program kept running.
    /// </summary>
    [Fact]
    public async Task A_process_the_caller_cancels_is_killed_rather_than_abandoned()
    {
        string marker = Path.Combine(Path.GetTempPath(), $"killmutants-test-{Guid.NewGuid():N}.marker");

        // Survives long enough to be cancelled, and leaves proof behind if it is left running.
        (string command, string[] arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", $"timeout /t 5 /nobreak > nul & type nul > \"{marker}\""])
            : ("/bin/sh", new[] { "-c", $"sleep 5; touch '{marker}'" });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProcessRunner.RunAsync(
                command, arguments, Path.GetTempPath(), TimeSpan.FromMinutes(5),
                cancellationToken: cancellation.Token));

        // Well past when the process would have written it, had it survived.
        await Task.Delay(TimeSpan.FromSeconds(7), TestContext.Current.CancellationToken);

        Assert.False(
            File.Exists(marker),
            "the cancelled process ran to completion, so it was never killed");
    }
}
