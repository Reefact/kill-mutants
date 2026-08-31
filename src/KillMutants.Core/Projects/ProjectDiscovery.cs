using System.Text.Json;
using KillMutants.Processes;

namespace KillMutants.Projects;

/// <summary>
/// Finds the test project in a directory tree and the project it exercises.
/// </summary>
/// <remarks>
/// Milestone 1 handles exactly one test project and one project under test, and says so plainly
/// rather than guessing. Multi-project solutions are milestone 3.
/// </remarks>
internal sealed class ProjectDiscovery
{
    private static readonly TimeSpan BuildBudget = TimeSpan.FromMinutes(10);

    private readonly MsBuildQuery _msBuild;
    private readonly string _configuration;

    public ProjectDiscovery(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _configuration = configuration;
        _msBuild = new MsBuildQuery(configuration);
    }

    /// <summary>Discovers the target, building it so that its output exists on disk.</summary>
    public async Task<MutationTestTarget> DiscoverAsync(
        string searchDirectory,
        CancellationToken cancellationToken = default)
    {
        string testProjectPath = await FindTestProjectAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        await BuildAsync(testProjectPath, cancellationToken).ConfigureAwait(false);

        string projectUnderTestPath = await FindProjectUnderTestAsync(testProjectPath, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> testProperties = await _msBuild
            .GetPropertiesAsync(testProjectPath, ["TargetPath", "TargetDir"], cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> properties = await _msBuild
            .GetPropertiesAsync(projectUnderTestPath, ["TargetFileName"], cancellationToken)
            .ConfigureAwait(false);

        var projectUnderTest = new ProjectUnderTest(
            projectUnderTestPath,
            Path.GetDirectoryName(projectUnderTestPath)!,
            properties["TargetFileName"]);

        var testProject = new TestProject(
            testProjectPath,
            testProperties["TargetPath"],
            testProperties["TargetDir"]);

        return new MutationTestTarget(projectUnderTest, testProject);
    }

    /// <summary>
    /// Builds the test project once, so the test application and every dependency exist before any
    /// mutant is injected. Nothing may run MSBuild after injection: both <c>dotnet build</c> and
    /// <c>dotnet test</c> copy the pristine assembly back over the mutant.
    /// </summary>
    private async Task BuildAsync(string projectPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync(
                "dotnet",
                ["build", projectPath, $"-c", _configuration, "-nologo"],
                Path.GetDirectoryName(projectPath)!,
                BuildBudget,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ProjectAnalysisException(
                $"The test project failed to build, so there is nothing to mutate." +
                $"{Environment.NewLine}{result.CombinedOutput}");
        }
    }

    private async Task<string> FindTestProjectAsync(string searchDirectory, CancellationToken cancellationToken)
    {
        string[] candidates = Directory
            .EnumerateFiles(searchDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path, searchDirectory))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ProjectAnalysisException($"No C# project was found under '{searchDirectory}'.");
        }

        List<string> testProjects = [];

        foreach (string candidate in candidates)
        {
            if (await IsXunitTestProjectAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                testProjects.Add(candidate);
            }
        }

        return testProjects.Count switch
        {
            1 => testProjects[0],
            0 => throw new ProjectAnalysisException(
                $"No xUnit test project was found under '{searchDirectory}'. " +
                "KillMutants supports xUnit 4 on Microsoft Testing Platform 2 " +
                "(the xunit.v3 package family at version 4)."),
            _ => throw new ProjectAnalysisException(
                $"Found {testProjects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"test projects under '{searchDirectory}'. KillMutants currently handles exactly one." +
                $"{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", testProjects)}"),
        };
    }

    private async Task<bool> IsXunitTestProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> packages = await GetItemIdentitiesAsync(
            projectPath, "PackageReference", cancellationToken).ConfigureAwait(false);

        return packages.Any(package => package.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> FindProjectUnderTestAsync(string testProjectPath, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> references = await GetItemIdentitiesAsync(
            testProjectPath, "ProjectReference", cancellationToken).ConfigureAwait(false);

        string testProjectDirectory = Path.GetDirectoryName(testProjectPath)!;

        string[] resolved = [.. references
            .Select(reference => Path.GetFullPath(Path.Combine(testProjectDirectory, reference)))];

        return resolved.Length switch
        {
            1 => resolved[0],
            0 => throw new ProjectAnalysisException(
                $"'{Path.GetFileName(testProjectPath)}' references no project, so there is nothing to mutate."),
            _ => throw new ProjectAnalysisException(
                $"'{Path.GetFileName(testProjectPath)}' references " +
                $"{resolved.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} projects. " +
                "KillMutants currently mutates exactly one."),
        };
    }

    private async Task<IReadOnlyList<string>> GetItemIdentitiesAsync(
        string projectPath,
        string itemName,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync(
                "dotnet",
                ["msbuild", projectPath, $"-p:Configuration={_configuration}", "-nologo", $"-getItem:{itemName}"],
                Path.GetDirectoryName(projectPath)!,
                BuildBudget,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return [];
        }

        using JsonDocument json = JsonDocument.Parse(result.StandardOutput);

        if (!json.RootElement.TryGetProperty("Items", out JsonElement items) ||
            !items.TryGetProperty(itemName, out JsonElement values))
        {
            return [];
        }

        return [.. values.EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())
            .Where(identity => !string.IsNullOrEmpty(identity))
            .Select(identity => identity!)];
    }

    private static bool IsUnderBuildOutput(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
