using System.Text.Json;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// The log exists for the run that never writes its report, so what matters is what is on disk the
/// moment a verdict has been told - not what the file holds once it is closed (DEC0014).
/// </summary>
public sealed class VerdictLogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static IReadOnlyList<Mutant> MutantsFor(string source) =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From(source, "/src/Ages.cs"));

    /// <summary>Read while the log is still open, as a step after a cancelled run would read it.</summary>
    private static string[] ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Each_verdict_is_on_disk_as_soon_as_it_is_told()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        using VerdictLog log = VerdictLog.Create(path);

        log.Report(new MutantResult(mutants[0], MutantStatus.Killed));

        Assert.Single(ReadWhileOpen(path));

        log.Report(new MutantResult(mutants[1], MutantStatus.Survived));

        string[] lines = ReadWhileOpen(path);

        Assert.Equal(2, lines.Length);

        using JsonDocument second = JsonDocument.Parse(lines[1]);

        Assert.Equal(mutants[1].Key.ToString(), second.RootElement.GetProperty("key").GetString());
        Assert.Equal("Survived", second.RootElement.GetProperty("status").GetString());
    }

    /// <summary>A line left by an earlier run is a verdict about code that may no longer exist.</summary>
    [Fact]
    public void Creating_the_log_empties_whatever_an_earlier_run_left()
    {
        string path = Path.Combine(_directory, "report.jsonl");

        File.WriteAllText(path, "{\"key\":\"from an earlier run\"}\n");

        using VerdictLog log = VerdictLog.Create(path);

        Assert.Empty(ReadWhileOpen(path));
    }

    [Fact]
    public void Lines_end_with_a_line_feed_on_every_platform()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        string path = Path.Combine(_directory, "report.jsonl");

        using (VerdictLog log = VerdictLog.Create(path))
        {
            log.Report(new MutantResult(mutant, MutantStatus.Killed));
        }

        string content = File.ReadAllText(path);

        Assert.EndsWith("}\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', content);
    }

    [Fact]
    public void The_directory_it_goes_in_is_created()
    {
        string path = Path.Combine(_directory, "artifacts", "nested", "report.jsonl");

        using VerdictLog log = VerdictLog.Create(path);

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("report.json", "report.jsonl")]
    [InlineData("report", "report.jsonl")]
    [InlineData("mutation.report.json", "mutation.report.jsonl")]
    public void The_log_takes_the_reports_name_with_its_own_extension(string report, string expected)
    {
        Assert.Equal(
            Path.Combine(_directory, expected),
            VerdictLog.PathBeside(Path.Combine(_directory, report)));
    }

    /// <summary>
    /// A run that continues a series (DEC0015) adds to the log rather than emptying it: the lines
    /// already there are what it is about to inherit.
    /// </summary>
    [Fact]
    public void Appending_keeps_the_lines_an_earlier_run_left()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        using (VerdictLog first = VerdictLog.Create(path))
        {
            first.Report(new MutantResult(mutants[0], MutantStatus.Killed));
        }

        using (VerdictLog second = VerdictLog.Append(path))
        {
            second.Report(new MutantResult(mutants[1], MutantStatus.Survived));
        }

        IReadOnlyList<LoggedVerdict> read = VerdictLog.Read(path);

        Assert.Equal(
            [mutants[0].Key.ToString(), mutants[1].Key.ToString()],
            read.Select(verdict => verdict.Key));
        Assert.Equal([MutantStatus.Killed, MutantStatus.Survived], read.Select(verdict => verdict.Status));
    }

    [Fact]
    public void Reading_an_absent_log_gives_nothing()
    {
        Assert.Empty(VerdictLog.Read(Path.Combine(_directory, "nothing.jsonl")));
    }

    /// <summary>A run killed mid-write leaves a torn last line, which is not a verdict.</summary>
    [Fact]
    public void Reading_skips_a_last_line_cut_short()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        using (VerdictLog log = VerdictLog.Create(path))
        {
            log.Report(new MutantResult(mutants[0], MutantStatus.Killed));
        }

        File.AppendAllText(path, """{"key":"7de901333e8920b0","sta""");

        Assert.Single(VerdictLog.Read(path));
    }

    /// <summary>
    /// Torn means physically unfinished. A last line that is not a verdict but ends in a line feed is
    /// complete, and tolerating it once would only move the refusal: <c>Append</c> keeps a line that
    /// ends in a line feed, the next verdict lands after it, and the resume after that finds it in
    /// the middle of the file. Refused at the first read instead, with the same line named both
    /// times - the two resumes are played out here to show it.
    /// </summary>
    [Fact]
    public void A_complete_last_line_that_is_not_a_verdict_is_refused_at_the_first_resume_not_the_second()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        File.WriteAllText(
            path,
            JsonReportWriter.Line(new MutantResult(mutants[0], MutantStatus.Killed)) + "\nnot a verdict\n");

        // The first resume reads before it appends, and is refused here.
        IOException first = Assert.Throws<IOException>(() => VerdictLog.Read(path));

        Assert.Contains("line 2", first.Message, StringComparison.Ordinal);

        // Had it appended anyway, the line would be in the middle, and the second resume would be
        // refused over the same line - not a torn one, and not a later one.
        using (VerdictLog log = VerdictLog.Append(path))
        {
            log.Report(new MutantResult(mutants[1], MutantStatus.Survived));
        }

        Assert.Equal(3, File.ReadAllLines(path).Length);

        IOException second = Assert.Throws<IOException>(() => VerdictLog.Read(path));

        Assert.Contains("line 2", second.Message, StringComparison.Ordinal);
    }

    /// <summary>The line feed is what tells a torn line from a complete one - whatever the content.</summary>
    [Fact]
    public void Only_a_last_line_without_a_line_feed_is_taken_for_torn()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");
        string verdict = JsonReportWriter.Line(new MutantResult(mutants[0], MutantStatus.Killed));

        File.WriteAllText(path, verdict + "\n" + verdict[..^3]);

        Assert.Single(VerdictLog.Read(path));

        File.WriteAllText(path, verdict + "\n" + verdict[..^3] + "\n");

        Assert.Throws<IOException>(() => VerdictLog.Read(path));
    }

    [Fact]
    public void Reading_refuses_a_line_that_is_not_a_verdict_and_names_it()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        File.WriteAllText(path, "not a verdict\n" + JsonReportWriter.Line(new MutantResult(mutants[0], MutantStatus.Killed)) + "\n");

        IOException refusal = Assert.Throws<IOException>(() => VerdictLog.Read(path));

        Assert.Contains("line 1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(path, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Appended to as it stood, a torn last line would have the next verdict glued onto it, and the
    /// run after that would refuse the whole log over a line in its middle that is not a verdict.
    /// </summary>
    [Fact]
    public void Appending_after_a_torn_last_line_cuts_the_fragment_first()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        using (VerdictLog first = VerdictLog.Create(path))
        {
            first.Report(new MutantResult(mutants[0], MutantStatus.Killed));
        }

        File.AppendAllText(path, """{"key":"7de901333e8920b0","sta""");

        using (VerdictLog second = VerdictLog.Append(path))
        {
            second.Report(new MutantResult(mutants[1], MutantStatus.Survived));
        }

        IReadOnlyList<LoggedVerdict> read = VerdictLog.Read(path);

        Assert.Equal(
            [mutants[0].Key.ToString(), mutants[1].Key.ToString()],
            read.Select(verdict => verdict.Key));
        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void Appending_to_a_log_with_no_line_feed_at_all_starts_it_over()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");
        string path = Path.Combine(_directory, "report.jsonl");

        File.WriteAllText(path, """{"key":"7de901333e8920b0","sta""");

        using (VerdictLog log = VerdictLog.Append(path))
        {
            log.Report(new MutantResult(mutants[0], MutantStatus.Killed));
        }

        Assert.Single(VerdictLog.Read(path));
    }
}
