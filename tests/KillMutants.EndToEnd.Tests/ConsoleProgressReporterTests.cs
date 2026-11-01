using KillMutants.Cli;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What a redirected log shows of a run, which is all a CI job keeps of one that was cancelled. The
/// clock is driven by hand, so a minute costs nothing and the assertions do not depend on how fast
/// the machine running them is.
/// </summary>
public class ConsoleProgressReporterTests : IDisposable
{
    private readonly ManualClock _clock = new();
    private readonly StringWriter _log = new();

    public void Dispose()
    {
        _log.Dispose();
        GC.SuppressFinalize(this);
    }

    private ConsoleProgressReporter Redirected() => Reporter(ProgressMode.Log);

    private ConsoleProgressReporter Reporter(ProgressMode mode, int width = 0) =>
        new(_log, mode, _clock, TimeSpan.FromSeconds(30), () => width, watchForSlowMutants: false);

    /// <summary>The lines written, without the time each one starts with - see <see cref="Each_line_of_the_log_is_dated"/>.</summary>
    private string[] Lines() => [.. Dated().Select(line => line.Length >= 10 && line[2] == ':' ? line[10..] : line.TrimStart())];

    private string[] Dated() =>
        _log.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static MutantInProgress Mutant(string id, int line = 440) =>
        new(id, "Conditional", "Rendering/Renderer.cs", line, Budget: TimeSpan.FromSeconds(11));

    private static MutationTestProgress Testing(int completed) =>
        new(MutationTestPhase.TestingMutants, completed, 816);

    [Fact]
    public void A_phase_is_announced_once_and_without_its_count()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(1));
        progress.Report(Testing(2));
        progress.Report(Testing(3));

        Assert.Equal(["Testing mutants"], Lines());
    }

    [Fact]
    public void The_count_is_written_again_once_a_minute_has_passed_and_not_before()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(1));
        _clock.Advance(ConsoleProgressReporter.Interval - TimeSpan.FromSeconds(1));
        progress.Report(Testing(100));
        _clock.Advance(TimeSpan.FromSeconds(1));
        progress.Report(Testing(340));
        progress.Report(Testing(341));

        Assert.Equal(["Testing mutants", "Testing mutants 340/816"], Lines());
    }

    /// <summary>
    /// The minute is counted from the last line written, so an hour of testing is sixty lines however
    /// many mutants it judged - the thousands of lines a log was spared stay spared.
    /// </summary>
    [Fact]
    public void Each_count_written_starts_the_next_minute()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(1));

        for (int minute = 1; minute <= 3; minute++)
        {
            _clock.Advance(ConsoleProgressReporter.Interval);
            progress.Report(Testing(minute * 100));
            progress.Report(Testing((minute * 100) + 1));
        }

        Assert.Equal(
            [
                "Testing mutants",
                "Testing mutants 100/816",
                "Testing mutants 200/816",
                "Testing mutants 300/816",
            ],
            Lines());
    }

    [Fact]
    public void A_phase_with_nothing_to_count_is_never_written_twice()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(new MutationTestProgress(MutationTestPhase.VerifyingBaseline, Subject: "Slugger"));
        _clock.Advance(TimeSpan.FromMinutes(5));
        progress.Report(new MutationTestProgress(MutationTestPhase.VerifyingBaseline, Subject: "Slugger"));

        Assert.Equal(["Verifying the baseline — Slugger"], Lines());
    }

    [Fact]
    public void A_new_phase_is_announced_at_once_whatever_the_clock_says()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(1));
        progress.Report(new MutationTestProgress(MutationTestPhase.ConfirmingTimeouts, 0, 1, "M105"));

        Assert.Equal(["Testing mutants", "Confirming timeouts — M105"], Lines());
    }

    /// <summary>
    /// Regression test (#52). A phase that goes through several projects is announced without the
    /// first one's name: in a log, that line is still the last one standing when the nineteenth
    /// project fails, and it named a project that was fine. The count lines that follow name the
    /// project they are on.
    /// </summary>
    [Fact]
    public void A_phase_over_several_items_is_announced_without_the_first_ones_name()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(new MutationTestProgress(MutationTestPhase.Discovering, 0, 19, "Sample.Library.Tests"));
        _clock.Advance(ConsoleProgressReporter.Interval);
        progress.Report(new MutationTestProgress(MutationTestPhase.Discovering, 18, 19, "FloorCheck"));

        Assert.Equal(["Discovering projects", "Discovering projects 18/19 — FloorCheck"], Lines());
    }

    /// <summary>
    /// Review of #55. A total of 0 means "not known yet", which cannot tell one item from many, so
    /// a phase that goes through items is announced without the first one's name whatever its total.
    /// </summary>
    [Fact]
    public void A_phase_over_items_with_an_unknown_total_is_announced_without_the_first_ones_name()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(new MutationTestProgress(MutationTestPhase.Discovering, 0, 0, "First.Tests"));

        Assert.Equal(["Discovering projects"], Lines());
    }

    /// <summary>#65: a phase that stops writing says when it stopped, so every line carries its time.</summary>
    [Fact]
    public void Each_line_of_the_log_is_dated()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(1));
        _clock.Advance(ConsoleProgressReporter.Interval);
        progress.Report(Testing(95));

        Assert.Equal(["01:37:16  Testing mutants", "01:38:16  Testing mutants 95/816"], Dated());
    }

    /// <summary>#65: a counter that stops moving says nothing until it names the mutant in progress.</summary>
    [Fact]
    public void A_count_in_the_log_names_the_mutant_in_progress()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(0) with { Mutant = Mutant("M1") });
        progress.Report(Testing(1) with { Mutant = Mutant("M1") with { Status = Mutations.MutantStatus.Killed } });
        progress.Report(Testing(1) with { Mutant = Mutant("M2", line: 97) });
        _clock.Advance(ConsoleProgressReporter.Interval);
        progress.Report(Testing(1) with { Mutant = Mutant("M3", line: 12) });

        Assert.Equal(
            ["Testing mutants", "Testing mutants 1/816  Conditional  Rendering/Renderer.cs:12"],
            Lines());
    }

    /// <summary>
    /// #65: a part set aside (DEC0022) is written the moment it happens, with the first line of its
    /// reason, in every mode - the final report is too late to learn it.
    /// </summary>
    [Theory]
    [InlineData("log")]
    [InlineData("live")]
    [InlineData("none")]
    public void A_part_set_aside_is_written_at_once_in_every_mode(string mode)
    {
        using ConsoleProgressReporter progress = Reporter(CommandLineOptions.ProgressModeOf(mode)!.Value);

        progress.Report(new MutationTestProgress(
            MutationTestPhase.Discovering,
            SetAside: new SetAside(
                SetAsideStage.Discovery,
                "tools/floor-check/FloorCheck.csproj",
                "requested SDK 8.0.100 is not installed\nInstall it, or leave the project out.")));

        Assert.Contains("01:37:16  Set aside — tools/floor-check/FloorCheck.csproj (could not be read)", Dated());
        Assert.Contains("requested SDK 8.0.100 is not installed; the report has the rest", Lines());
    }

    [Fact]
    public void None_writes_nothing_else()
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.None);

        progress.Report(Testing(0) with { Mutant = Mutant("M1") });
        _clock.Advance(TimeSpan.FromMinutes(5));
        progress.Report(Testing(1));
        progress.CheckForSlowMutants();

        Assert.Empty(_log.ToString());
    }

    /// <summary>
    /// #65: a mutant running past the threshold is named once, with its budget, and one that finished
    /// in time is never named - which is what tells a slow mutant from a stuck run.
    /// </summary>
    [Fact]
    public void A_slow_mutant_is_named_once_with_its_budget()
    {
        using ConsoleProgressReporter progress = Redirected();

        progress.Report(Testing(0) with { Mutant = Mutant("M1") });
        progress.Report(Testing(0) with { Mutant = Mutant("M2", line: 97) });
        _clock.Advance(TimeSpan.FromSeconds(29));
        progress.CheckForSlowMutants();
        progress.Report(Testing(1) with { Mutant = Mutant("M2", line: 97) with { Status = Mutations.MutantStatus.Survived } });
        _clock.Advance(TimeSpan.FromSeconds(1));
        progress.CheckForSlowMutants();
        _clock.Advance(TimeSpan.FromSeconds(30));
        progress.CheckForSlowMutants();

        Assert.Equal(
            ["Testing mutants", "Slow — Conditional  Rendering/Renderer.cs:440  running for 30s (budget 11s)"],
            Lines());
    }

    /// <summary>
    /// Review of #73: a timeout re-run alone is where a test host the budget did not bring down
    /// shows, so a mutant confirmed that way is watched like any other - and its second verdict is
    /// not counted again among the verdicts.
    /// </summary>
    [Fact]
    public void A_mutant_confirmed_alone_is_watched_and_not_counted_twice()
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.Live);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 1, Mutant: Mutant("M1")));
        progress.Report(new MutationTestProgress(
            MutationTestPhase.TestingMutants, 1, 1, Mutant: Mutant("M1") with { Status = Mutations.MutantStatus.Timeout }));
        progress.Report(new MutationTestProgress(MutationTestPhase.ConfirmingTimeouts, 0, 1, "M1", Mutant: Mutant("M1")));
        _clock.Advance(TimeSpan.FromSeconds(30));
        progress.CheckForSlowMutants();
        progress.Report(new MutationTestProgress(
            MutationTestPhase.ConfirmingTimeouts, 1, 1, "M1", Mutant: Mutant("M1") with { Status = Mutations.MutantStatus.Killed }));
        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 1, 1));

        string written = _log.ToString();

        Assert.Contains("Slow — Conditional  Rendering/Renderer.cs:440  running for 30s", written, StringComparison.Ordinal);
        Assert.EndsWith("killed 0 · survived 0 · no coverage 0 · timed out 1", written.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// #65: the live line is rewritten in place - no line break - with a bar of plain characters, the
    /// count, the mutant in progress and the verdicts so far.
    /// </summary>
    [Fact]
    public void The_live_line_is_rewritten_in_place_with_the_mutant_in_progress()
    {
        using (ConsoleProgressReporter progress = Reporter(ProgressMode.Live))
        {
            progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));
            _clock.Advance(TimeSpan.FromSeconds(10));
            progress.Report(new MutationTestProgress(
                MutationTestPhase.TestingMutants, 1, 4, Mutant: Mutant("M1") with { Status = Mutations.MutantStatus.Killed }));
            progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 1, 4, Mutant: Mutant("M2", line: 97)));

            string drawn = _log.ToString();
            string last = drawn[(drawn.LastIndexOf('\r') + 1)..];

            Assert.DoesNotContain('\n', drawn);
            Assert.StartsWith(
                "Testing mutants  [#####---------------] 1/4  25%  10s  ~30s left  Conditional  Rendering/Renderer.cs:97  killed 1",
                last,
                StringComparison.Ordinal);
        }

        Assert.EndsWith("\r", _log.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// With several workers, finishing the mutant started last leaves the line naming the one still
    /// running, not nothing.
    /// </summary>
    [Fact]
    public void The_live_line_names_a_mutant_still_running_when_the_last_one_started_finishes()
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.Live);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));
        _clock.Advance(TimeSpan.FromSeconds(1));
        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M2", line: 97)));
        progress.Report(new MutationTestProgress(
            MutationTestPhase.TestingMutants, 1, 4, Mutant: Mutant("M2", line: 97) with { Status = Mutations.MutantStatus.Killed }));

        string drawn = _log.ToString();

        Assert.Contains("Rendering/Renderer.cs:440", drawn[(drawn.LastIndexOf('\r') + 1)..], StringComparison.Ordinal);
    }

    /// <summary>
    /// Review of #73: on an 80-column terminal the mutant's file and line are what must survive, so
    /// the verdicts and the bar are left out before it is.
    /// </summary>
    [Theory]
    [InlineData(80, "Conditional  Rendering/Renderer.cs:97")]
    [InlineData(40, "Renderer.cs:97")]
    public void The_mutant_in_progress_survives_a_narrow_terminal(int width, string kept)
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.Live, width);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));
        _clock.Advance(TimeSpan.FromSeconds(10));
        progress.Report(new MutationTestProgress(
            MutationTestPhase.TestingMutants, 1, 4, Mutant: Mutant("M1") with { Status = Mutations.MutantStatus.Killed }));
        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 1, 4, Mutant: Mutant("M2", line: 97)));

        string drawn = _log.ToString();
        string last = drawn[(drawn.LastIndexOf('\r') + 1)..].TrimEnd();

        Assert.Contains(kept, last, StringComparison.Ordinal);
        Assert.True(last.Length < width, last);
    }

    /// <summary>
    /// Review of #73: after the terminal shrinks from 100 columns to 40, the next line is padded to
    /// the new width, not the old one - padding to 99 would wrap, and every later rewrite would land
    /// on the wrong line.
    /// </summary>
    [Fact]
    public void A_terminal_that_shrinks_is_never_written_past_its_new_width()
    {
        int width = 100;
        using ConsoleProgressReporter progress = new(
            _log, ProgressMode.Live, _clock, TimeSpan.FromSeconds(30), () => width, watchForSlowMutants: false);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));
        int before = _log.ToString().Length;
        width = 40;
        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M2", line: 97)));

        string written = _log.ToString()[before..];

        Assert.StartsWith("\r", written, StringComparison.Ordinal);
        Assert.True(written.Length - 1 <= 39, $"{written.Length - 1} characters written at 40 columns");
    }

    /// <summary>A line wider than the terminal would wrap, and a wrapped line cannot be rewritten.</summary>
    [Fact]
    public void The_live_line_fits_the_terminal()
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.Live, width: 30);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));

        Assert.Equal(29, _log.ToString().TrimStart('\r').Length);
    }

    /// <summary>A notice is written above the live line, which is then drawn again below it.</summary>
    [Fact]
    public void A_notice_in_live_mode_is_written_above_the_line_which_comes_back()
    {
        using ConsoleProgressReporter progress = Reporter(ProgressMode.Live);

        progress.Report(new MutationTestProgress(MutationTestPhase.TestingMutants, 0, 4, Mutant: Mutant("M1")));
        _clock.Advance(TimeSpan.FromSeconds(31));
        progress.CheckForSlowMutants();

        // Split on the writer's own line break: a lone carriage return is how the live line is
        // rewritten, and treating it as a line ending would hide exactly what is being tested.
        string[] lines = _log.ToString().Split(_log.NewLine);

        Assert.EndsWith("01:37:47  Slow — Conditional  Rendering/Renderer.cs:440  running for 31s (budget 11s)", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Testing mutants", lines[1].TrimStart('\r'), StringComparison.Ordinal);
    }

    private sealed class ManualClock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 1, 1, 37, 16, TimeSpan.Zero);

        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => Start.AddTicks(_ticks);

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
