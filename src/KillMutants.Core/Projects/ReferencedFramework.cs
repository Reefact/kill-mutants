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
        (library.TargetsSeveralFrameworks ? library.TargetFrameworks : [library.TargetFramework])
        .Any(candidate => string.Equals(candidate, framework, StringComparison.OrdinalIgnoreCase));

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
        if (!library.TargetsSeveralFrameworks)
        {
            return library.TargetFramework;
        }

        string[] candidates = [.. library.TargetFrameworks];

        if (testProject.ProjectAssetsFile.Length == 0 || !File.Exists(testProject.ProjectAssetsFile))
        {
            return null;
        }

        string? negotiated = NegotiatedFramework(testProject, library);

        if (negotiated is null)
        {
            return null;
        }

        return candidates.FirstOrDefault(candidate =>
            string.Equals(WithoutPlatform(candidate), negotiated, StringComparison.OrdinalIgnoreCase));
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
