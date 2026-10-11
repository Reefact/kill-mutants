using System.Globalization;
using System.Text;

namespace KillMutants.Cli;

/// <summary>
/// A length of time written the way a CI configuration writes one: <c>85m</c>, <c>90s</c>,
/// <c>1h30m</c>.
/// </summary>
/// <remarks>
/// <para>
/// A unit is required on every number. A bare <c>85</c> would have to mean seconds or minutes, and a
/// deadline read in the wrong one is a run that stops almost at once or one that never stops in
/// time - silently, in both cases, and on the job that can least afford it.
/// </para>
/// <para>
/// The longest accepted is what the timer underneath can hold, about twenty-four days. Nothing
/// shorter is ruled out on the grounds that it seems unreasonable: a deadline of a few seconds is
/// exactly what a test of this option needs.
/// </para>
/// </remarks>
internal static class Duration
{
    /// <summary>What a valid value looks like, for messages refusing one.</summary>
    public const string Expected = "a duration such as 85m, 90s or 1h30m, no longer than 24 days";

    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Reads <paramref name="text"/>, or says it cannot.</summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Hours, then minutes, then seconds, each at most once: "30m1h" is more likely a slip than a
        // deliberate way of writing ninety minutes, and refusing it costs nothing.
        const string units = "hms";
        int nextUnit = 0;
        long seconds = 0;
        string remaining = text.Trim();

        while (remaining.Length > 0)
        {
            int digits = 0;

            while (digits < remaining.Length && char.IsAsciiDigit(remaining[digits]))
            {
                digits++;
            }

            if (digits == 0 || digits == remaining.Length)
            {
                return false;
            }

            int unit = units.IndexOf(char.ToLowerInvariant(remaining[digits]), nextUnit);

            if (unit < 0 ||
                !long.TryParse(
                    remaining.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture,
                    out long amount) ||
                amount > Longest.TotalSeconds)
            {
                return false;
            }

            seconds += amount * (unit switch { 0 => 3600, 1 => 60, _ => 1 });
            nextUnit = unit + 1;
            remaining = remaining[(digits + 1)..];
        }

        if (seconds <= 0 || seconds > Longest.TotalSeconds)
        {
            return false;
        }

        value = TimeSpan.FromSeconds(seconds);

        return true;
    }

    /// <summary>Writes a duration back in the form <see cref="TryParse"/> reads.</summary>
    public static string Format(TimeSpan value)
    {
        var text = new StringBuilder();

        Append((long)value.TotalHours, 'h');
        Append(value.Minutes, 'm');
        Append(value.Seconds, 's');

        return text.Length == 0 ? "0s" : text.ToString();

        void Append(long amount, char unit)
        {
            if (amount > 0)
            {
                text.Append(amount.ToString(CultureInfo.InvariantCulture)).Append(unit);
            }
        }
    }
}
