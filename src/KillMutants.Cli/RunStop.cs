using System.Runtime.InteropServices;

namespace KillMutants.Cli;

/// <summary>
/// Turns a deadline, or the first interrupt signal, into a request that the run stop and report what
/// it judged - and remembers which of the two it was.
/// </summary>
/// <remarks>
/// <para>
/// Both lead to the same place (DEC0013), but the deadline is the one that works in CI. A job's
/// timeout signals the step's shell and kills the whole tree seconds later, so a tool started from
/// that shell never hears about it; a deadline the tool sets itself does not depend on hearing
/// anything. The signals are for a person pressing Ctrl-C, and for whatever does deliver one -
/// <c>timeout --signal=INT</c>, an <c>exec</c>'d step, a container runtime.
/// </para>
/// <para>
/// Only the first signal is taken over. A second is left to do what it always does, because someone
/// pressing Ctrl-C twice wants the process gone, and a tool that would not let them is a tool they
/// learn to kill harder.
/// </para>
/// </remarks>
internal sealed class RunStop : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly List<PosixSignalRegistration> _signals = [];
    private readonly Timer? _deadline;
    private int _signalsReceived;
    private string? _reason;

    public RunStop(TimeSpan? deadline)
    {
        foreach (PosixSignal signal in (PosixSignal[])[PosixSignal.SIGINT, PosixSignal.SIGTERM])
        {
            try
            {
                _signals.Add(PosixSignalRegistration.Create(signal, OnSignal));
            }
            catch (PlatformNotSupportedException)
            {
                // A platform without this signal cannot send it either, so nothing is lost.
            }
        }

        if (deadline is { } after)
        {
            _deadline = new Timer(
                _ => Stop($"its {Duration.Format(after)} deadline (--stop-after)"),
                state: null,
                dueTime: after,
                period: Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Signalled when the run should stop and report what it has.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>What stopped the run, as a message says it, or null when nothing has.</summary>
    public string? Reason => Volatile.Read(ref _reason);

    /// <inheritdoc />
    public void Dispose()
    {
        _deadline?.Dispose();

        foreach (PosixSignalRegistration signal in _signals)
        {
            signal.Dispose();
        }

        _source.Dispose();
    }

    private void OnSignal(PosixSignalContext context)
    {
        if (Interlocked.Increment(ref _signalsReceived) > 1)
        {
            return;
        }

        context.Cancel = true;
        Stop(context.Signal.ToString());
    }

    /// <summary>Asks for the stop, once, keeping the first reason given.</summary>
    /// <remarks>
    /// Cancelled asynchronously. Cancelling runs whatever is waiting on the token, and here that is
    /// the whole run coming down - test hosts being killed, the report being assembled. None of that
    /// belongs on the thread that delivers a signal, which the runtime expects back promptly.
    /// </remarks>
    private void Stop(string reason)
    {
        if (Interlocked.CompareExchange(ref _reason, reason, null) is not null)
        {
            return;
        }

        try
        {
            _ = _source.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The deadline fell, or a signal landed, as the tool was finishing: the run is over and
            // there is nothing left to stop. Letting this escape a timer callback would end the
            // process on its way out.
        }
    }
}
