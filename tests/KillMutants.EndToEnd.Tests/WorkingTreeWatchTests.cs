using System.Diagnostics;
using KillMutants.Git;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Regression tests (#51). A test that writes next to its own source - Verify's <c>.received</c>
/// files - left files in the measured working copy, and nothing said they came from the run.
/// </summary>
public sealed class WorkingTreeWatchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("killmutants-watch-").FullName;

    /// <remarks>
    /// Git writes its objects read-only, and on Windows <see cref="Directory.Delete(string, bool)"/>
    /// refuses a read-only file: the first CI run of these tests failed there in teardown, every
    /// assertion having passed. The attribute is cleared first; whatever is still held is left, as
    /// <c>FixtureCopy</c> leaves it.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_file_written_during_the_run_is_reported()
    {
        await CommittedWorkingCopyAsync();
        WorkingTreeWatch watch = (await WorkingTreeWatch.StartAsync(_root, TestContext.Current.CancellationToken))!;

        Write("Tests/Snapshots.TheCatalog.received.txt");

        Assert.Equal(
            ["Tests/Snapshots.TheCatalog.received.txt"],
            await watch.NewFilesAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>What was untracked before the run, and what git ignores, did not come from it.</summary>
    [Fact]
    public async Task Files_that_were_already_there_or_are_ignored_are_not_reported()
    {
        await CommittedWorkingCopyAsync();
        Write("notes.txt");
        WorkingTreeWatch watch = (await WorkingTreeWatch.StartAsync(_root, TestContext.Current.CancellationToken))!;

        Write("bin/Release/output.dll");
        File.AppendAllText(Path.Combine(_root, "notes.txt"), "more");

        Assert.Empty(await watch.NewFilesAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>The report and its companions are the tool's own, written on purpose.</summary>
    [Fact]
    public async Task The_tools_own_report_files_are_not_reported()
    {
        await CommittedWorkingCopyAsync();
        WorkingTreeWatch watch = (await WorkingTreeWatch.StartAsync(_root, TestContext.Current.CancellationToken))!;

        string report = Write("reports/km.json");

        Assert.Empty(await watch.NewFilesAsync([report], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Outside_a_working_copy_there_is_nothing_to_watch()
    {
        Assert.Null(await WorkingTreeWatch.StartAsync(_root, TestContext.Current.CancellationToken));
    }

    private async Task CommittedWorkingCopyAsync()
    {
        Write("src/Library.cs");
        await File.WriteAllTextAsync(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
        await GitAsync("init", "-q");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "fixture");
    }

    private string Write(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");

        return path;
    }

    private async Task GitAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in (string[])["-c", "user.name=KillMutants", "-c", "user.email=tests@example.invalid", .. arguments])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }
}
