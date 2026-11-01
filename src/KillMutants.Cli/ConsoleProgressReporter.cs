using System.Globalization;
using System.Text;
using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.Cli;

/// <summary>What the console shows of a run while it runs, chosen with <c>--progress</c>.</summary>
/// <remarks>
/// <para>
/// The mode is the user's to choose, and nothing about the terminal is detected (DEC0023). Whether
/// standard error is redirected does not say who reads it: a terminal can sit inside a container
/// whose output is captured, and a CI log is read by a person. A guess is wrong in both directions
/// and leaves whoever it surprised nothing to change.
/// </para>
/// </remarks>
internal enum ProgressMode
{
    /// <summary>Dated lines, nothing rewritten in place: for a CI log, a file, a pipe - the default.</summary>
    Log,

    /// <summary>One line rewritten in place, naming the mutant in progress: for a person at a terminal.</summary>
    Live,

    /// <summary>Nothing but the parts set aside: for a tool that reads the JSON report.</summary>
    None,
}

/// <summary>Shows where a run has got to, in the mode the user chose.</summary>
/// <remarks>
/// <para>
/// A run takes minutes, and silence is indistinguishable from a hang. In <see cref="ProgressMode.Live"/>
/// the line is rewritten in place. In <see cref="ProgressMode.Log"/> each phase is announced once,
/// because thousands of carriage returns in a log file help nobody.
/// </para>
/// <para>
/// Announcing a phase once is not enough on its own, and a CI log is where that showed. A run whose
/// mutant testing took fifty-seven minutes printed <c>Testing mutants</c> and then nothing until
/// the job was cancelled, so its log could not say whether the run had been at 800 of 816 or stuck
/// on the first. So a counted phase is also written again with its count, at most once per
/// <see cref="Interval"/>: an hour costs sixty lines, not thousands. The clock is read only when
/// something is reported, so a phase that stops making progress stops writing, and the last count
/// written says where it stopped.
/// </para>
/// <para>
/// What a counter cannot say, #65 found by following a 75-minute run: which mutant is being tested,
/// whether a slow one is slow or stuck, when anything happened, and that a part was set aside. So a
/// line in the log is dated and names the mutant in progress, and two events are written the moment
/// they happen rather than at the next count or in the final report: a part set aside (DEC0022), and
/// a mutant that has been running for longer than <c>--slow</c>, once for that mutant.
/// </para>
/// <para>
/// None of these lines is an interface. The JSON report is the channel for tools, and the help says
/// so; the lines are for people and may change between versions (DEC0023).
/// </para>
/// </remarks>
internal sealed class ConsoleProgressReporter : IProgress<MutationTestProgress>, IDisposable
{
    /// <summary>The least time between two lines for the same phase, in the log.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How long a mutant runs before it is named as slow, unless <c>--slow</c> says otherwise.</summary>
    internal static readonly TimeSpan DefaultSlow = TimeSpan.FromSeconds(30);

    /// <summary>How often the mutants in progress are looked at for one that has become slow.</summary>
    private static readonly TimeSpan SlowCheck = TimeSpan.FromSeconds(1);

    /// <summary>The width of the time a line starts with, so a continuation lines up under its text.</summary>
    private const string Indent = "          ";

    private readonly TextWriter _writer;
    private readonly ProgressMode _mode;
    private readonly TimeProvider _time;
    private readonly TimeSpan _slowAfter;
    private readonly Func<int> _width;
    private readonly ITimer? _timer;
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<MutantStatus, int> _reached = [];

    private MutationTestPhase? _announced;
    private long _lastWritten;
    private long _phaseStarted;
    private MutationTestProgress? _shown;
    private int _lastWidth;
    private bool _disposed;

    /// <param name="writer">Standard error, usually.</param>
    /// <param name="mode">What the user asked to see.</param>
    /// <param name="time">The clock, so a minute costs nothing in a test.</param>
    /// <param name="slowAfter">How long a mutant runs before it is named as slow.</param>
    /// <param name="width">
    /// The width the live line must fit in, or 0 when it is not known; a line wider than the terminal
    /// wraps, and a wrapped line can no longer be rewritten in place.
    /// </param>
    /// <param name="watchForSlowMutants">
    /// False to leave the look for slow mutants to <see cref="CheckForSlowMutants"/>, as a test does.
    /// </param>
    public ConsoleProgressReporter(
        TextWriter writer,
        ProgressMode mode,
        TimeProvider? time = null,
        TimeSpan? slowAfter = null,
        Func<int>? width = null,
        bool watchForSlowMutants = true)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
        _mode = mode;
        _time = time ?? TimeProvider.System;
        _slowAfter = slowAfter ?? DefaultSlow;
        _width = width ?? (() => 0);

        if (mode != ProgressMode.None && watchForSlowMutants)
        {
            _timer = _time.CreateTimer(_ => CheckForSlowMutants(), null, SlowCheck, SlowCheck);
        }
    }

    /// <inheritdoc />
    public void Report(MutationTestProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (value.SetAside is { } part)
            {
                Notice(SetAsideLines(part));

                return;
            }

            if (_mode == ProgressMode.None)
            {
                return;
            }

            Track(value);

            if (_mode == ProgressMode.Live)
            {
                Draw(value);
            }
            else
            {
                Log(value);
            }
        }
    }

    /// <summary>
    /// Names, once each, the mutants that have been running for longer than the threshold.
    /// </summary>
    /// <remarks>
    /// Called by a timer rather than when something is reported, because a stuck run is precisely one
    /// that reports nothing. The budget a mutant was given is named beside it: a mutant well past its
    /// budget is one whose test host the timeout did not bring down, which is a different finding from
    /// a mutant that is merely slow.
    /// </remarks>
    internal void CheckForSlowMutants()
    {
        lock (_gate)
        {
            if (_disposed || _mode == ProgressMode.None)
            {
                return;
            }

            foreach (Running running in _running.Values.Where(running => !running.Named).OrderBy(running => running.Started))
            {
                TimeSpan elapsed = _time.GetElapsedTime(running.Started);

                if (elapsed < _slowAfter)
                {
                    continue;
                }

                running.Named = true;

                MutantInProgress mutant = running.Mutant;
                string budget = mutant.Budget is { } given ? $" (budget {Seconds(given)})" : string.Empty;

                Notice([$"Slow — {Name(mutant)}  running for {Seconds(elapsed)}{budget}"]);
            }
        }
    }

    /// <summary>Stops looking for slow mutants and clears the live line, so the report starts clean.</summary>
    public void Dispose()
    {
        _timer?.Dispose();

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Clear();
        }
    }

    private void Track(MutationTestProgress value)
    {
        if (_announced != value.Phase)
        {
            _phaseStarted = _time.GetTimestamp();
        }

        if (value.Mutant is not { } mutant)
        {
            return;
        }

        if (mutant.Status is { } status)
        {
            _running.Remove(mutant.Id);
            _reached[status] = _reached.GetValueOrDefault(status) + 1;
        }
        else
        {
            _running[mutant.Id] = new Running(mutant, _time.GetTimestamp());
        }
    }

    private void Log(MutationTestProgress value)
    {
        if (_announced != value.Phase)
        {
            // Announced without its count: what the first report of a phase counts is how far it
            // had got when it began, and the line written a minute later is the one that says how
            // far it has got.
            //
            // And without its subject when that subject names an item rather than the phase: it is
            // then the first of the items the phase goes through, and in a log it is the line still
            // on screen when something fails. A discovery that stopped on the nineteenth project was
            // announced as "Discovering projects — <the first>", which pointed at a project that was
            // fine (#52). Whether a subject names an item is the phase's to say, not the total's: a
            // total of 0 means "not known yet", so it cannot tell one item from many (review of
            // #55). A phase of exactly one item is still named by it.
            _announced = value.Phase;
            _lastWritten = _time.GetTimestamp();
            _writer.WriteLine(Stamp() + Describe(value with
            {
                Completed = 0,
                Total = 0,
                Subject = NamesAnItem(value.Phase) && value.Total != 1 ? null : value.Subject,
            }));
        }
        else if (value.IsCounted && _time.GetElapsedTime(_lastWritten) >= Interval)
        {
            _lastWritten = _time.GetTimestamp();
            _writer.WriteLine(Stamp() + Describe(value) + InProgress());
        }
    }

    /// <summary>Rewrites the one live line: the phase, how far, how fast, and the mutant in progress.</summary>
    private void Draw(MutationTestProgress value)
    {
        _announced = value.Phase;
        _shown = value;

        var line = new StringBuilder(Describe(value with { Completed = 0, Total = 0 }));
        TimeSpan elapsed = _time.GetElapsedTime(_phaseStarted);

        if (value.IsCounted)
        {
            int percent = (int)(100L * value.Completed / value.Total);

            line.Append(CultureInfo.InvariantCulture, $"  {Bar(value.Completed, value.Total)} {Format(value.Completed)}/{Format(value.Total)}  {percent}%");
        }

        line.Append("  ").Append(Duration.Format(Whole(elapsed)));

        if (value.IsCounted && value.Completed > 0 && value.Completed < value.Total)
        {
            TimeSpan left = Whole(elapsed * ((double)(value.Total - value.Completed) / value.Completed));

            // Under a second says nothing a reader can act on, and "~0s left" reads as done.
            if (left > TimeSpan.Zero)
            {
                line.Append("  ~").Append(Duration.Format(left)).Append(" left");
            }
        }

        line.Append(InProgress());

        if (value.Phase == MutationTestPhase.TestingMutants && _reached.Count > 0)
        {
            line.Append(CultureInfo.InvariantCulture,
                $"  killed {Count(MutantStatus.Killed)} · survived {Count(MutantStatus.Survived)} · " +
                $"no coverage {Count(MutantStatus.NoCoverage)} · timed out {Count(MutantStatus.Timeout)}");
        }

        string text = line.ToString();
        int width = Width();

        // One column short of the terminal: a line that reaches the last column moves the cursor to
        // the next one on some terminals, and the next carriage return then rewrites the wrong line.
        if (width > 1 && text.Length >= width)
        {
            text = text[..(width - 1)];
        }

        _writer.Write('\r' + text.PadRight(_lastWidth));
        _lastWidth = text.Length;
    }

    /// <summary>
    /// Writes lines that have to be read now - a part set aside, a slow mutant - dated, out of the way
    /// of the live line, which is drawn again below them.
    /// </summary>
    private void Notice(List<string> lines)
    {
        Clear();

        for (int index = 0; index < lines.Count; index++)
        {
            _writer.WriteLine((index == 0 ? Stamp() : Indent) + lines[index]);
        }

        if (_mode == ProgressMode.Live && _shown is { } shown)
        {
            Draw(shown);
        }
    }

    private void Clear()
    {
        if (_mode == ProgressMode.Live && _lastWidth > 0)
        {
            _writer.Write('\r' + new string(' ', _lastWidth) + '\r');
            _lastWidth = 0;
        }
    }

    /// <summary>
    /// The part, what stopped it, and the first line of the reason: enough to know at once what was
    /// lost, with the rest left to the report, which has the way out.
    /// </summary>
    private static List<string> SetAsideLines(SetAside part)
    {
        string what = part.Stage switch
        {
            SetAsideStage.Discovery => "could not be read",
            SetAsideStage.Build => "did not build",
            SetAsideStage.Analysis => "could not be analysed",
            SetAsideStage.Baseline => "its baseline is red",
            _ => "its coverage could not be measured",
        };

        string[] reason = part.Reason.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<string> lines = [$"Set aside — {part.Name} ({what})"];

        if (reason.Length > 0)
        {
            lines.Add(reason[0] + (reason.Length > 1 ? "; the report has the rest" : string.Empty));
        }

        return lines;
    }

    /// <summary>
    /// The mutant most recently started of those still being tested, or nothing. With several
    /// workers, the one that started last is the one a reader has not seen yet; one stuck for longer
    /// is named on its own line by <see cref="CheckForSlowMutants"/>.
    /// </summary>
    private string InProgress() =>
        _running.Count == 0
            ? string.Empty
            : "  " + Name(_running.Values.MaxBy(running => running.Started)!.Mutant);

    private static string Name(MutantInProgress mutant) =>
        $"{mutant.Family}  {mutant.Path}:{Format(mutant.Line)}";

    private string Stamp() =>
        _time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ";

    private int Width()
    {
        try
        {
            return _width();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            return 0;
        }
    }

    private string Count(MutantStatus status) => Format(_reached.GetValueOrDefault(status));

    /// <summary>A bar of plain characters, which every terminal and every log can show.</summary>
    private static string Bar(int completed, int total)
    {
        const int Cells = 20;
        int filled = (int)((long)Cells * Math.Min(completed, total) / total);

        return "[" + new string('#', filled) + new string('-', Cells - filled) + "]";
    }

    private static TimeSpan Whole(TimeSpan value) => TimeSpan.FromSeconds(Math.Floor(value.TotalSeconds));

    private static string Seconds(TimeSpan value) => Duration.Format(Whole(value) > TimeSpan.Zero ? Whole(value) : TimeSpan.FromSeconds(1));

    /// <summary>
    /// True for a phase whose subject is the item it is on - a project, a target, a mutant - and
    /// false for one whose subject names the phase itself: the target a baseline is verified for,
    /// the revision a partial run compares from.
    /// </summary>
    private static bool NamesAnItem(MutationTestPhase phase) =>
        phase is not (MutationTestPhase.VerifyingBaseline or MutationTestPhase.SelectingChanges);

    private static string Describe(MutationTestProgress progress)
    {
        string what = progress.Phase switch
        {
            MutationTestPhase.Discovering => "Discovering projects",
            MutationTestPhase.SelectingChanges => "Reading the base revision",
            MutationTestPhase.Building => "Building test projects",
            MutationTestPhase.Analysing => "Analysing",
            MutationTestPhase.VerifyingBaseline => "Verifying the baseline",
            MutationTestPhase.MeasuringCoverage => "Measuring coverage",
            MutationTestPhase.TestingMutants => "Testing mutants",
            MutationTestPhase.ConfirmingTimeouts => "Confirming timeouts",
            MutationTestPhase.ReVerifyingKills => "Re-verifying kills",
            _ => progress.Phase.ToString(),
        };

        string counted = progress.IsCounted
            ? $" {Format(progress.Completed)}/{Format(progress.Total)}"
            : string.Empty;

        string subject = progress.Subject is null ? string.Empty : $" — {progress.Subject}";

        return $"{what}{counted}{subject}";
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A mutant being tested: when it started, and whether it was already named as slow.</summary>
    private sealed class Running(MutantInProgress mutant, long started)
    {
        public MutantInProgress Mutant { get; } = mutant;

        public long Started { get; } = started;

        public bool Named { get; set; }
    }
}
