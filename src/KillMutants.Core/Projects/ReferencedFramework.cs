using System.Text.Json;

namespace KillMutants.Projects;

/// <summary>
/// The framework a test project actually loads a referenced project for, as NuGet negotiated it.
/// </summary>
/// <remarks>
/// <para>
/// Not the test project's own framework. A <c>net10.0</c> suite referencing a library that targets
/// <c>netstandard2.0</c> loads the <c>netstandard2.0</c> build, and one referencing a library that
/// targets <c>netstandard2.0;net8.0</c> loads <c>net8.0</c>. Asking the library about the suite's
/// framework, as discovery first did, fails on the first (the library has no such target) and is
/// merely lucky on any library that happens to list it.
/// </para>
/// <para>
/// Read from <c>project.assets.json</c> rather than recomputed. The nearest-framework rule is
/// NuGet's, it has more cases than a table here would hold, and the file records its verdict per
/// project reference - transitive ones included. The cost is that the suite must have been restored,
/// which a run needs anyway to know it is a test project at all. Anything this cannot answer returns
/// <see langword="null"/> and the caller keeps the suite's own framework, which is what it did before.
/// </para>
/// </remarks>
internal static class ReferencedFramework
{
    /// <summary>
    /// True when <paramref name="library"/> is built for <paramref name="framework"/> itself, in which
    /// case that build is the one a suite on that framework loads and nothing needs negotiating.
    /// </summary>
    public static bool Declares(ProjectFacts library, string framework) =>
        FrameworksOf(library).Any(candidate =>
            string.Equals(candidate, framework, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every framework <paramref name="library"/> is built for. A project written with the plural
    /// property and one entry, <c>&lt;TargetFrameworks&gt;net10.0&lt;/TargetFrameworks&gt;</c>, has no
    /// singular answer in the evaluation that read it, so the one entry is the framework.
    /// </summary>
    private static IReadOnlyList<string> FrameworksOf(ProjectFacts library) =>
        library.TargetFrameworks.Count > 0
            ? library.TargetFrameworks
            : string.IsNullOrEmpty(library.TargetFramework) ? [] : [library.TargetFramework];

    /// <summary>
    /// The target framework moniker, among those <paramref name="library"/> declares, that
    /// <paramref name="testProject"/> loads it for, or <see langword="null"/> when that cannot be told.
    /// </summary>
    public static string? For(ProjectFacts testProject, ProjectFacts library)
    {
        // One framework is no choice at all, and needs no file to say so. It is also the only answer
        // when NuGet's file has no entry for the library: the project graph MSBuild walks and the one
        // NuGet restored are not always the same graph (a reference under a condition, or one that
        // the restore leaves out), and a library reached only the first way still has to be built.
        IReadOnlyList<string> candidates = FrameworksOf(library);

        if (candidates.Count <= 1)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        if (testProject.ProjectAssetsFile.Length == 0 || !File.Exists(testProject.ProjectAssetsFile))
        {
            return null;
        }

        string? negotiated = NegotiatedFramework(testProject, library);

        if (negotiated is null)
        {
            return null;
        }

        string[] sharingTheBase = [.. candidates.Where(candidate =>
            string.Equals(WithoutPlatform(candidate), negotiated, StringComparison.OrdinalIgnoreCase))];

        if (sharingTheBase.Length <= 1)
        {
            return sharingTheBase.FirstOrDefault();
        }

        // net10.0 and net10.0-windows both reduce to net10.0, and the restore result does not say
        // which was chosen: its framework for a reference never carries the platform, measured with a
        // net10.0-windows suite over a library offering both. NuGet's own order is the suite's platform
        // first, then the platform-neutral build, which is compatible with any suite. Anything else is
        // ambiguous, and the caller keeps the suite's framework rather than have this pick the first.
        string platform = PlatformOf(testProject.TargetFramework);

        string[] samePlatform = [.. sharingTheBase.Where(candidate =>
            string.Equals(PlatformOf(candidate), platform, StringComparison.OrdinalIgnoreCase))];

        if (samePlatform.Length == 1)
        {
            return samePlatform[0];
        }

        string[] neutral = [.. sharingTheBase.Where(candidate => PlatformOf(candidate).Length == 0)];

        return neutral.Length == 1 ? neutral[0] : null;
    }

    /// <summary>The platform part of a framework, without its version: <c>windows</c> for <c>net10.0-windows7.0</c>.</summary>
    private static string PlatformOf(string framework)
    {
        int dash = framework.IndexOf('-', StringComparison.Ordinal);

        return dash < 0 ? string.Empty : framework[(dash + 1)..].TrimEnd('.', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    }

    private static string? NegotiatedFramework(ProjectFacts testProject, ProjectFacts library)
    {
        try
        {
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(testProject.ProjectAssetsFile));
            JsonElement root = assets.RootElement;

            if (!root.TryGetProperty("libraries", out JsonElement libraries) ||
                !root.TryGetProperty("targets", out JsonElement targets))
            {
                return null;
            }

            string? key = null;

            foreach (JsonProperty entry in libraries.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "project" &&
                    entry.Value.TryGetProperty("path", out JsonElement path) &&
                    ProjectPaths.Comparer.Equals(
                        Path.GetFullPath(path.GetString() ?? string.Empty, testProject.Directory),
                        library.ProjectPath))
                {
                    key = entry.Name;
                    break;
                }
            }

            if (key is null)
            {
                return null;
            }

            // The suite's own framework alias is the target's name; a runtime-specific variant of it
            // ("net10.0/linux-x64") would be a second answer to the same question and is not asked.
            foreach (JsonProperty target in targets.EnumerateObject())
            {
                if (target.Name.Contains('/') ||
                    !string.Equals(
                        WithoutPlatform(target.Name), WithoutPlatform(testProject.TargetFramework),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (target.Value.TryGetProperty(key, out JsonElement resolved) &&
                    resolved.TryGetProperty("framework", out JsonElement framework))
                {
                    return ToMoniker(framework.GetString());
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An assets file that cannot be read is the same as one that is absent: no answer.
        }

        return null;
    }

    private static string WithoutPlatform(string framework)
    {
        int dash = framework.IndexOf('-', StringComparison.Ordinal);

        return dash < 0 ? framework : framework[..dash];
    }

    /// <summary>
    /// <c>.NETStandard,Version=v2.0</c> as <c>netstandard2.0</c>, the spelling a project file uses.
    /// </summary>
    internal static string? ToMoniker(string? framework)
    {
        if (framework is null)
        {
            return null;
        }

        string[] parts = framework.Split(",Version=v");

        if (parts.Length != 2 || !Version.TryParse(parts[1], out Version? version))
        {
            return null;
        }

        string major = version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string minor = version.Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (parts[0])
        {
            case ".NETStandard":
                return $"netstandard{major}.{minor}";
            case ".NETCoreApp":
                return version.Major >= 5 ? $"net{major}.{minor}" : $"netcoreapp{major}.{minor}";
            case ".NETFramework":
                string build = version.Build > 0
                    ? version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty;

                return $"net{major}{minor}{build}";
            default:
                return null;
        }
    }
}
