using System.Text.Json;
using KillMutants.Mutations;
using KillMutants.Testing;

namespace KillMutants.Reporting;

/// <summary>One verdict read back from the log an earlier run wrote.</summary>
/// <param name="Key">The mutant's stable identity, which a plan row and a fresh mutant join on.</param>
/// <param name="Status">What became of it.</param>
/// <param name="Detail">Why, when the status needed explaining.</param>
/// <param name="KilledBy">The tests that failed against it, empty for a kill no test is named for.</param>
/// <param name="Overturned">How an earlier verdict was corrected by a re-run alone, or null.</param>
/// <remarks>
/// Only what a later run needs to inherit the verdict is read: the rest of the line - position,
/// text, family - is the plan's and the fresh mutant's. A disagreement is never on a line (DEC0014),
/// so none is read here; the series keeps those in the plan.
/// </remarks>
public sealed record LoggedVerdict(
    string Key,
    MutantStatus Status,
    string? Detail,
    IReadOnlyList<TestName> KilledBy,
    string? Overturned)
{
    /// <summary>Reads one line of the log.</summary>
    /// <exception cref="JsonException">The line is not JSON.</exception>
    /// <exception cref="FormatException">The line is JSON but not a verdict.</exception>
    public static LoggedVerdict Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("key", out JsonElement key) || key.GetString() is not { Length: > 0 } keyText ||
            !root.TryGetProperty("status", out JsonElement status) ||
            !Enum.TryParse(status.GetString(), ignoreCase: false, out MutantStatus parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new FormatException("The line names no mutant key, or no status this tool produces.");
        }

        IReadOnlyList<TestName> killedBy =
            root.TryGetProperty("killedBy", out JsonElement tests) && tests.ValueKind == JsonValueKind.Array
                ? [.. tests.EnumerateArray()
                    .Select(test => test.GetString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => TestName.Create(name!))]
                : [];

        return new LoggedVerdict(keyText, parsed, Text(root, "detail"), killedBy, Text(root, "overturned"));
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
