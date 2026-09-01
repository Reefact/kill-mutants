using System.Globalization;

namespace KillMutants.Reporting;

/// <summary>The time a mutant's test run is allowed, and everything it was derived from.</summary>
/// <param name="Baseline">How long the unmutated suite took, measured with nothing else running.</param>
/// <param name="Factor">What that duration is multiplied by.</param>
/// <param name="Margin">What is added on top of it.</param>
/// <remarks>
/// <para>
/// The budget alone is not enough to explain a timeout after the fact, and a run that calibrates it
/// badly can report most of a component as detected without a single test having failed. A field
/// report on another tool described exactly that: 2 505 of 4 575 mutants counted as detected purely
/// because they timed out, a headline of 100%, and a budget the tool never wrote down anywhere - so
/// the first day of the investigation went on reconstructing what the limit had even been.
/// </para>
/// <para>
/// Publishing the three inputs rather than the answer is what makes the answer checkable. A reader
/// who knows the suite takes five seconds can see at a glance whether a budget of forty-six is
/// generous or absurd, and whether a timeout says something about the mutant or about the machine.
/// </para>
/// </remarks>
public sealed record TimeBudget(TimeSpan Baseline, double Factor, TimeSpan Margin)
{
    /// <summary>The limit itself.</summary>
    public TimeSpan Budget => TimeSpan.FromSeconds(Baseline.TotalSeconds * Factor) + Margin;

    /// <summary>Renders as the formula it is, so a reader can check the arithmetic.</summary>
    public override string ToString()
    {
        var culture = CultureInfo.InvariantCulture;

        return
            $"{Seconds(Budget)} s " +
            $"({Seconds(Baseline)} s baseline alone × {Factor.ToString("0.#", culture)} " +
            $"+ {Seconds(Margin)} s)";
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
}
