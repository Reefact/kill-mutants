using System.Text;

namespace KillMutants.Reporting;

/// <summary>
/// Appends each verdict to a JSON Lines file the moment it is final, so a run that never gets as far
/// as writing its report still leaves what it judged behind it.
/// </summary>
/// <remarks>
/// <para>
/// The report is written once, at the end, and a run can end without getting there: a CI job
/// cancelled because a newer push superseded it, a runner that was lost, a deadline the run overran.
/// None of those reaches this process as anything it can act on - on GitHub Actions the signal goes
/// to the step's shell, and the tree is killed seconds later - so the only verdicts that outlive
/// them are the ones already on disk. See DEC0014.
/// </para>
/// <para>
/// One line per mutant, in the shape the report gives each entry of <c>mutants</c>, and flushed as
/// it is written. Flushed to the operating system rather than to the disk: a process that is killed
/// loses nothing the kernel already holds, and forcing every verdict through to the device would
/// cost a sync per mutant to guard against a power cut, which is not what this is for.
/// </para>
/// <para>
/// The file is emptied when the log is created, because a line left over from an earlier run is a
/// verdict about code that may no longer exist, and nothing in the file could tell a reader which
/// lines were which. It is kept when the run ends: by then the report holds the same verdicts and
/// more, and deleting a file someone may be reading would be a surprise with nothing to gain.
/// </para>
/// </remarks>
public sealed class VerdictLog : IProgress<MutantResult>, IDisposable
{
    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    private VerdictLog(string path, StreamWriter writer)
    {
        _path = path;
        _writer = writer;
    }

    /// <summary>Where the log goes for a report written to <paramref name="reportPath"/>.</summary>
    /// <remarks>
    /// Derived rather than configured, so a job that already names its report has its log without
    /// asking, and a reader who finds one file knows where to look for the other.
    /// </remarks>
    public static string PathBeside(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        return Path.ChangeExtension(reportPath, ".jsonl");
    }

    /// <summary>Creates the log at <paramref name="path"/>, emptying any file already there.</summary>
    /// <exception cref="IOException">The file could not be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be created.</exception>
    public static VerdictLog Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Shared for reading, so a job can follow it while the run goes on and a step that runs
        // after a cancelled one can read what was there.
        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        // '\n' on every platform: a JSON Lines file is one format, not one per operating system.
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n",
        };

        return new VerdictLog(fullPath, writer);
    }

    /// <summary>Appends one verdict, and flushes it before returning.</summary>
    /// <exception cref="IOException">The line could not be written.</exception>
    public void Report(MutantResult value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string line = JsonReportWriter.Line(value);

        lock (_gate)
        {
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (IOException exception)
            {
                // Named, because the run reporting this failure has many files open and the
                // operating system's message names none of them.
                throw new IOException(
                    $"The verdict log '{_path}' could not be written: {exception.Message}", exception);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
