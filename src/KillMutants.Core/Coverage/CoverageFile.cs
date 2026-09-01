using System.Globalization;
using KillMutants.Mutations;

namespace KillMutants.Coverage;

/// <summary>Reads back what the recorder wrote during one test run.</summary>
internal static class CoverageFile
{
    /// <summary>
    /// The sites the recorder wrote down, or null when the file does not describe a finished run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An absent file is a real answer, not a missing one: the recorder creates its file the moment
    /// it is first touched, so never having been touched means the test entered no mutation site.
    /// </para>
    /// <para>
    /// A file that exists <em>without</em> the completion marker is the opposite - a measurement that
    /// started and did not finish - as is one containing anything the recorder would not have
    /// written. Both answer null, because a partial list of hits under-reports coverage in exactly
    /// the direction that produces mutants nobody runs.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MutantId>? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return null;
        }

        if (lines.Length == 0 || lines[^1] != CoverageProbe.CompletionMarker)
        {
            return null;
        }

        List<MutantId> reached = [];

        foreach (string line in lines[..^1])
        {
            if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return null;
            }

            reached.Add(MutantId.FromValue(value));
        }

        return reached;
    }
}
