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
/// The file is emptied when a run starts a series, because a line left over from an earlier run is a
/// verdict about code that may no longer exist, and nothing in the file could tell a reader which
/// lines were which. It is continued instead when a run resumes a series (DEC0015): the caller has
/// established, from the plan beside it, that every line in it was reached on the same code with
/// the same settings and tool, which is exactly what the emptying could not know. It is kept when
/// the run ends: by then the report holds the same verdicts and more, and deleting a file someone
/// may be reading would be a surprise with nothing to gain.
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
    public static VerdictLog Create(string path) => Open(path, FileMode.Create);

    /// <summary>
    /// Opens the log at <paramref name="path"/> to add to it, keeping every line already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a run that continues a series: the lines already there were reached on the same code and
    /// are about to be inherited, so emptying them would throw away what the run is resuming from.
    /// Whether that is so is the caller's to establish before opening - see DEC0015.
    /// </para>
    /// <para>
    /// A run killed mid-write leaves a last line cut short. Appended to as it stands, the next
    /// verdict would be glued onto the fragment, and the run after that would find a line in the
    /// middle of the file that is not a verdict and refuse the whole log. Every complete line ends
    /// in a line feed, so the fragment is whatever follows the last one, and it is cut before the
    /// first new line is written. <see cref="Read"/> skips the same fragment for the same reason.
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">The file could not be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be opened.</exception>
    public static VerdictLog Append(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        CutTornLastLine(Path.GetFullPath(path));

        return Open(path, FileMode.Append);
    }

    /// <summary>Reads back every verdict a log holds.</summary>
    /// <remarks>
    /// A run that is killed can leave a last line cut short, and a torn line is not a verdict - it
    /// is skipped, and the mutant it was about is judged again. Any other line that is not a verdict
    /// is something else entirely, and refusing is safer than guessing which lines to believe.
    /// </remarks>
    /// <returns>The verdicts, in the order they landed; empty when there is no file.</returns>
    /// <exception cref="IOException">The file holds a line that is not a verdict.</exception>
    public static IReadOnlyList<LoggedVerdict> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines = [.. File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line))];
        List<LoggedVerdict> verdicts = [];

        for (int index = 0; index < lines.Length; index++)
        {
            try
            {
                verdicts.Add(LoggedVerdict.Parse(lines[index]));
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or FormatException)
            {
                if (index == lines.Length - 1)
                {
                    break;
                }

                throw new IOException(
                    $"The verdict log '{path}' holds a line that is not a verdict (line " +
                    $"{(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}): " +
                    $"{exception.Message} Delete the plan beside it to start the series again.",
                    exception);
            }
        }

        return verdicts;
    }

    private static void CutTornLastLine(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        if (stream.Length == 0)
        {
            return;
        }

        stream.Position = stream.Length - 1;

        if (stream.ReadByte() == '\n')
        {
            return;
        }

        // Back to the byte after the last line feed, or to the start when there is none. A line is
        // short, so reading one byte at a time is the simplest thing that is fast enough.
        long keep = stream.Length - 1;

        while (keep > 0)
        {
            stream.Position = keep - 1;

            if (stream.ReadByte() == '\n')
            {
                break;
            }

            keep--;
        }

        stream.SetLength(keep);
    }

    private static VerdictLog Open(string path, FileMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Shared for reading, so a job can follow it while the run goes on and a step that runs
        // after a cancelled one can read what was there.
        var stream = new FileStream(fullPath, mode, FileAccess.Write, FileShare.Read);

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
