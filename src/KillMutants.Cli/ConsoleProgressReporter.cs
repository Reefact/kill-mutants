using System.Globalization;
using KillMutants.Reporting;

namespace KillMutants.Cli;

/// <summary>Shows where a run has got to, on one line.</summary>
/// <remarks>
/// <para>
/// A run takes minutes, and silence is indistinguishable from a hang. On a terminal the line is
/// rewritten in place; when the output is redirected - a CI log, a pipe - each phase is announced
/// once instead, because thousands of carriage returns in a log file help nobody.
/// </para>
/// <para>
/// Announcing a phase once is not enough on its own, and a CI log is where that showed. A run whose
/// mutant testing took fifty-seven minutes printed <c>Testing mutants</c> and then nothing until
/// the job was cancelled, so its log could not say whether the run had been at 800 of 816 or stuck
/// on the first. The silence the paragraph above rules out on a terminal had moved into the log. So
/// a counted phase is also written again with its count, at most once per <see cref="Interval"/>:
/// an hour costs sixty lines, not thousands. The clock is read only when something is reported, so
/// a phase that stops making progress stops writing, and the last count written says where it
/// stopped.
/// </para>
/// </remarks>
internal sealed class ConsoleProgressReporter : IProgress<MutationTestProgress>, IDisposable
{
    /// <summary>
    /// The least time between two lines for the same phase, when the output is redirected.
    /// </summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly TextWriter _writer;
    private readonly bool _rewritesInPlace;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private MutationTestPhase? _announced;
    private long _lastWritten;
    private int _lastWidth;

    public ConsoleProgressReporter(
        TextWriter writer, bool rewritesInPlace, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
        _rewritesInPlace = rewritesInPlace;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public void Report(MutationTestProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            if (_rewritesInPlace)
            {
                Rewrite(Describe(value));
            }
            else if (_announced != value.Phase)
            {
                // Announced without its count: what the first report of a phase counts is how
                // far it had got when it began, and the line written a minute later is the one
                // that says how far it has got.
                //
                // And without its subject when that subject names an item rather than the phase:
                // it is then the first of the items the phase goes through, and in a log it is the
                // line still on screen when something fails. A discovery that stopped on the
                // nineteenth project was announced as "Discovering projects — <the first>", which
                // pointed at a project that was fine (#52). Whether a subject names an item is the
                // phase's to say, not the total's: a total of 0 means "not known yet", so it cannot
                // tell one item from many (review of #55). A phase of exactly one item is still
                // named by it.
                _announced = value.Phase;
                _lastWritten = _time.GetTimestamp();
                _writer.WriteLine(Describe(value with
                {
                    Completed = 0,
                    Total = 0,
                    Subject = NamesAnItem(value.Phase) && value.Total != 1 ? null : value.Subject,
                }));
            }
            else if (value.IsCounted && _time.GetElapsedTime(_lastWritten) >= Interval)
            {
                _lastWritten = _time.GetTimestamp();
                _writer.WriteLine(Describe(value));
            }
        }
    }

    /// <summary>Clears the progress line, so the report that follows starts clean.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_rewritesInPlace && _lastWidth > 0)
            {
                _writer.Write('\r' + new string(' ', _lastWidth) + '\r');
                _lastWidth = 0;
            }
        }
    }

    private void Rewrite(string line)
    {
        _writer.Write('\r' + line.PadRight(_lastWidth));
        _lastWidth = line.Length;
    }

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
}
