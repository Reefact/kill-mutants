using System.Diagnostics;

namespace KillMutants.Processes;

/// <summary>Runs an external process to completion under a time budget, capturing both streams.</summary>
internal static class ProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> and waits for it to exit.</summary>
    /// <remarks>
    /// A mutation can turn a terminating loop into an endless one, so every external run is bounded.
    /// Exceeding the budget kills the whole process tree and is reported as
    /// <see cref="ProcessResult.TimedOut"/> rather than as an ordinary failure: the two mean very
    /// different things about a mutant.
    /// </remarks>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };

        process.Start();

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillQuietly(process);

            return new ProcessResult(-1, await ReadOrEmptyAsync(standardOutput).ConfigureAwait(false),
                await ReadOrEmptyAsync(standardError).ConfigureAwait(false), stopwatch.Elapsed, TimedOut: true);
        }

        stopwatch.Stop();

        return new ProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false),
            stopwatch.Elapsed,
            TimedOut: false);
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout firing and the kill. Nothing to do.
        }
    }

    private static async Task<string> ReadOrEmptyAsync(Task<string> stream)
    {
        try
        {
            return await stream.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
