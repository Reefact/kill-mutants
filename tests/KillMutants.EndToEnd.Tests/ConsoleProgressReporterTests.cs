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

    private ConsoleProgressReporter Redirected() => new(_log, rewritesInPlace: false, _clock);

    private string[] Lines() =>
        _log.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
