using System.Text.Encodings.Web;
using System.Text.Json;

namespace KillMutants.Reporting;

/// <summary>Reads and writes the plan beside a report.</summary>
/// <remarks>
/// <para>
/// Three files share a name: the report, the log of verdicts beside it (DEC0014) and the plan
/// (DEC0016). The two derived names are decided here and nowhere else, so a reader who finds one
/// file knows where the other two are, and a report cannot be given a name that is one of theirs.
/// </para>
/// <para>
/// The plan is rewritten several times in a run. Each rewrite goes to a sibling file first and is
/// then moved over the old one, so that what sits at the plan's path is always a whole document: a
/// run killed in the middle of a write leaves the previous plan, not half of a new one.
/// </para>
/// </remarks>
public sealed class RunPlanStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,

        // The mutated operators are the payload, as in the report, and escaping them would make the
        // plan unreadable and unsearchable for the same reason.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Names the three files from the report's path.</summary>
    public RunPlanStore(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        ReportPath = Path.GetFullPath(reportPath);
        LogPath = VerdictLog.PathBeside(ReportPath);
        PlanPath = PlanPathBeside(ReportPath);
    }

    /// <summary>Where the report goes.</summary>
    public string ReportPath { get; }

    /// <summary>Where each verdict lands as it is reached.</summary>
    public string LogPath { get; }

    /// <summary>Where the plan goes.</summary>
    public string PlanPath { get; }

    /// <summary>The three files, for whatever must leave them alone - a dirtiness read, say.</summary>
    public IReadOnlyList<string> Files => [ReportPath, LogPath, PlanPath];

    /// <summary>Where the plan goes for a report written to <paramref name="reportPath"/>.</summary>
    public static string PlanPathBeside(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        return Path.ChangeExtension(reportPath, ".plan.json");
    }

    /// <summary>The plan an earlier run left, or null when there is none.</summary>
    /// <exception cref="IOException">There is a file and it is not a plan this version can read.</exception>
    public RunPlan? ReadPlan()
    {
        if (!File.Exists(PlanPath))
        {
            return null;
        }

        RunPlan? plan;

        try
        {
            plan = JsonSerializer.Deserialize<RunPlan>(File.ReadAllText(PlanPath), Options);
        }
        catch (JsonException exception)
        {
            // Named, and not silently treated as absent: a plan that cannot be read is a series
            // whose state is unknown, and starting over would empty a log that may hold hours of
            // verdicts for a cause the user did not create. Deleting the file is the user's call.
            throw new IOException(
                $"The plan '{PlanPath}' could not be read: {exception.Message} Delete it to start " +
                "the series again.", exception);
        }

        if (plan is null || plan.Format != RunPlan.CurrentFormat)
        {
            throw new IOException(
                $"The plan '{PlanPath}' was written in a format this KillMutants does not read. " +
                "Delete it to start the series again.");
        }

        return plan;
    }

    /// <summary>Writes the plan, whole, over whatever was there.</summary>
    public void WritePlan(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Directory.CreateDirectory(Path.GetDirectoryName(PlanPath)!);

        string pending = PlanPath + ".tmp";

        File.WriteAllText(pending, JsonSerializer.Serialize(plan, Options));
        File.Move(pending, PlanPath, overwrite: true);
    }

    /// <summary>Removes the plan an earlier run left, for a run that starts a series afresh.</summary>
    public void DeletePlan()
    {
        if (File.Exists(PlanPath))
        {
            File.Delete(PlanPath);
        }
    }
}
