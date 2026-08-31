using System.Text.Json;
using KillMutants.Processes;

namespace KillMutants.Projects;

/// <summary>
/// Asks MSBuild about a project, using its structured <c>-getProperty</c> / <c>-getItem</c> output.
/// </summary>
/// <remarks>
/// This is the whole of KillMutants' project system integration. See
/// <c>docs/adr/0003-compilation-inputs-from-csc-command-line.md</c> for why there is no Buildalyzer,
/// no MSBuildWorkspace and no MSBuildLocator here.
/// </remarks>
internal sealed class MsBuildQuery
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    private readonly string _configuration;

    public MsBuildQuery(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _configuration = configuration;
    }

    /// <summary>Reads MSBuild properties from a project without building it.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string projectPath,
        IReadOnlyList<string> propertyNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        List<string> arguments = ["msbuild", projectPath, $"-p:Configuration={_configuration}", "-nologo"];
        arguments.AddRange(propertyNames.Select(name => $"-getProperty:{name}"));

        JsonDocument json = await RunAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        using (json)
        {
            if (!json.RootElement.TryGetProperty("Properties", out JsonElement properties))
            {
                throw new ProjectAnalysisException(
                    $"MSBuild returned no properties for '{projectPath}'.");
            }

            return properties.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Obtains the exact <c>csc</c> command line MSBuild would invoke for this project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SkipCompilerExecution</c> stops csc from actually running, and
    /// <c>ProvideCommandLineArgs</c> makes MSBuild publish the arguments it would have passed.
    /// A dedicated <c>IntermediateOutputPath</c> keeps every generated artefact out of the user's
    /// own <c>obj</c> directory, so analysing a project never disturbs their incremental build.
    /// </para>
    /// <para>
    /// If MSBuild decides the project is up to date it can skip <c>CoreCompile</c> and return
    /// nothing at all; <see cref="Analysis.CscCommandLine.Parse"/> would then happily produce an empty
    /// compilation, which is why the result is validated rather than trusted.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetCscCommandLineAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments =
        [
            "build", projectPath,
            $"-p:Configuration={_configuration}",
            "-t:Build",
            "-p:ProvideCommandLineArgs=true",
            "-p:SkipCompilerExecution=true",
            "-p:CopyBuildOutputToOutputDirectory=false",
            $"-p:IntermediateOutputPath={IntermediateOutputPath}",
            "-nologo",
            "-getItem:CscCommandLineArgs",
        ];

        JsonDocument json = await RunAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        using (json)
        {
            if (!json.RootElement.TryGetProperty("Items", out JsonElement items) ||
                !items.TryGetProperty("CscCommandLineArgs", out JsonElement argumentItems))
            {
                throw new ProjectAnalysisException(
                    $"MSBuild returned no compiler command line for '{projectPath}'. " +
                    "The project may not be a C# project, or may have failed to build.");
            }

            return [.. argumentItems.EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString())
                .Where(value => !string.IsNullOrEmpty(value))
                .Select(value => value!)];
        }
    }

    /// <summary>Where generated compiler inputs are written, relative to the analysed project.</summary>
    internal const string IntermediateOutputPath = "obj/killmutants/";

    private static async Task<JsonDocument> RunAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
                                  ?? Directory.GetCurrentDirectory();

        ProcessResult result = await ProcessRunner
            .RunAsync("dotnet", arguments, workingDirectory, Budget, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ProjectAnalysisException(
                $"MSBuild failed for '{projectPath}'.{Environment.NewLine}{result.CombinedOutput}");
        }

        try
        {
            return JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException exception)
        {
            throw new ProjectAnalysisException(
                $"Could not read MSBuild's output for '{projectPath}'.{Environment.NewLine}{result.StandardOutput}",
                exception);
        }
    }
}
