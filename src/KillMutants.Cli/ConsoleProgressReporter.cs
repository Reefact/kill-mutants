using System.Globalization;
using KillMutants.Reporting;

namespace KillMutants.Cli;

/// <summary>Shows where a run has got to, on one line.</summary>
/// <remarks>
/// A run takes minutes, and silence is indistinguishable from a hang. On a terminal the line is
/// rewritten in place; when the output is redirected - a CI log, a pipe - each phase is announced
/// once instead, because thousands of carriage returns in a log file help nobody.
/// </remarks>
internal sealed class ConsoleProgressReporter : IProgress<MutationTestProgress>, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _rewritesInPlace;
    private readonly Lock _gate = new();

    private MutationTestPhase? _announced;
    private int _lastWidth;

    public ConsoleProgressReporter(TextWriter writer, bool rewritesInPlace)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
        _rewritesInPlace = rewritesInPlace;
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
                _announced = value.Phase;
                _writer.WriteLine(Describe(value with { Completed = 0, Total = 0 }));
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

    private static string Describe(MutationTestProgress progress)
    {
        string what = progress.Phase switch
        {
            MutationTestPhase.Discovering => "Discovering projects",
            MutationTestPhase.Building => "Building test projects",
            MutationTestPhase.Analysing => "Analysing",
            MutationTestPhase.VerifyingBaseline => "Verifying the baseline",
            MutationTestPhase.MeasuringCoverage => "Measuring coverage",
            MutationTestPhase.TestingMutants => "Testing mutants",
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
