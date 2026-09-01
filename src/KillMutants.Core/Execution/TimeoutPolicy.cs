namespace KillMutants.Execution;

/// <summary>How long a mutant's test run may take before it is abandoned.</summary>
/// <param name="BaselineFactor">Multiplier applied to how long the unmutated suite took.</param>
/// <param name="Margin">Fixed allowance added on top, covering process start and warm-up.</param>
/// <remarks>
/// A mutation can turn a terminating loop into an endless one — changing <c>value = value + 1</c>
/// into <c>value - 1</c> is enough — so every run needs a deadline. The budget is derived from the
/// baseline rather than fixed, because what counts as "far too long" depends entirely on how long
/// the suite normally takes.
/// </remarks>
internal sealed record TimeoutPolicy(double BaselineFactor, TimeSpan Margin)
{
    /// <summary>
    /// Generous on purpose. A mutant wrongly reported as timed out hides a real gap in the tests,
    /// which is worse than waiting: the margin absorbs a slow machine or a noisy CI agent.
    /// </summary>
    public static TimeoutPolicy Default { get; } = new(BaselineFactor: 3.0, Margin: TimeSpan.FromSeconds(30));

    /// <summary>The budget to allow, given how long the unmutated suite took.</summary>
    /// <remarks>
    /// Returns the calculation rather than its result: a budget nobody can recompute is a number a
    /// reader has to take on trust, and a timeout is unexplainable without it.
    /// </remarks>
    public Reporting.TimeBudget For(TimeSpan baseline)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseline.Ticks);

        return new Reporting.TimeBudget(baseline, BaselineFactor, Margin);
    }
}
