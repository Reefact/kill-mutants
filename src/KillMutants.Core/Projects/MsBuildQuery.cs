using System.Text.Json;
using KillMutants.Processes;

namespace KillMutants.Projects;

/// <summary>
/// Asks MSBuild about a project, using its structured <c>-getProperty</c> / <c>-getItem</c> output.
/// </summary>
/// <remarks>
/// This is the whole of KillMutants' project system integration. See
/// DEC0003 in <c>docs/decisions</c> for why there is no Buildalyzer,
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

    /// <summary>
    /// Reads everything KillMutants needs about a project, in a single MSBuild invocation.
    /// </summary>
    /// <param name="projectPath">The project to inspect.</param>
    /// <param name="targetFramework">
    /// The framework to resolve against. Required for a project targeting several: without it
    /// MSBuild answers for an unspecified one, and mutants could be emitted against a framework the
    /// test project never loads.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <remarks>
    /// Batched deliberately. Asking separately would mean several process launches per project, and
    /// a solution of any size would spend most of a run starting MSBuild.
    /// </remarks>
    public async Task<ProjectFacts> GetProjectFactsAsync(
        string projectPath,
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["msbuild", projectPath, $"-p:Configuration={_configuration}", "-nologo"];

        if (!string.IsNullOrEmpty(targetFramework))
        {
            arguments.Add($"-p:TargetFramework={targetFramework}");
        }

        arguments.AddRange(
        [
            "-getProperty:TargetFileName",
            "-getProperty:TargetPath",
            "-getProperty:TargetDir",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:OutputType",
            "-getProperty:XunitTestProject",
            "-getProperty:KillMutantsTestSupport",
            "-getItem:PackageReference",
            "-getItem:ProjectReference",
        ]);

        string output = await RunRawAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        using JsonDocument json = ParseJson(projectPath, output);
        JsonElement root = json.RootElement;

        string Property(string name) =>
            root.TryGetProperty("Properties", out JsonElement properties) &&
            properties.TryGetProperty(name, out JsonElement value)
                ? value.GetString() ?? string.Empty
                : string.Empty;

        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

        return new ProjectFacts(
            ProjectPath: Path.GetFullPath(projectPath),
            AssemblyFileName: Property("TargetFileName"),
            AssemblyPath: Property("TargetPath"),
            OutputDirectory: Property("TargetDir"),
            TargetFramework: Property("TargetFramework"),
            TargetFrameworks: Split(Property("TargetFrameworks")),
            OutputType: Property("OutputType"),
            XunitTestProject: IsTrue(Property("XunitTestProject")),
            DeclaredTestSupport: IsTrue(Property("KillMutantsTestSupport")),
            PackageReferences: ReadItems(root, "PackageReference", identity => identity),
            ProjectReferences: ReadItems(
                root,
                "ProjectReference",
                identity => Path.GetFullPath(Path.Combine(directory, identity)),
                RunsAtRunTime));
    }

    /// <summary>Reads an MSBuild boolean, which is written in whatever case the author felt like.</summary>
    private static bool IsTrue(string value) =>
        string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Makes MSBuild re-run <c>CoreCompile</c>, by removing the cache file its incremental check
    /// reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious alternative - redirecting <c>IntermediateOutputPath</c> so the outputs are
    /// missing - is a global property, so it propagates to every referenced project and makes the
    /// command line point at reference assemblies the compiler was never allowed to produce. That
    /// breaks any project with a project reference.
    /// </para>
    /// <para>
    /// This file is a cache MSBuild regenerates, so deleting it is safe. It does leave the project
    /// marked out of date, meaning the user's next build recompiles it - which would happen anyway,
    /// since <c>SkipCompilerExecution</c> means our own query never produces the outputs either.
    /// </para>
    /// </remarks>
    private static void ForceCompileToRun(string projectPath)
    {
        string objDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "obj");

        if (!Directory.Exists(objDirectory))
        {
            return;
        }

        foreach (string cache in Directory.EnumerateFiles(
                     objDirectory, "*.CoreCompileInputs.cache", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(cache);
            }
            catch (IOException)
            {
                // Another build holds it. The empty-command-line guard still covers us.
            }
        }
    }

    private static IReadOnlyList<string> Split(string value) =>
        [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static IReadOnlyList<string> ReadItems(
        JsonElement root,
        string itemName,
        Func<string, string> project,
        Func<JsonElement, bool>? keep = null)
    {
        if (!root.TryGetProperty("Items", out JsonElement items) ||
            !items.TryGetProperty(itemName, out JsonElement values))
        {
            return [];
        }

        return [.. values.EnumerateArray()
            .Where(item => keep is null || keep(item))
            .Select(item => item.GetProperty("Identity").GetString())
            .Where(identity => !string.IsNullOrEmpty(identity))
            .Select(identity => project(identity!))];
    }

    /// <summary>
    /// True when a project reference contributes an assembly the tests will actually load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source generator or analyzer is referenced with <c>OutputItemType="Analyzer"</c> and
    /// <c>ReferenceOutputAssembly="false"</c>: it runs inside the compiler at build time and its
    /// assembly never reaches the test project's output directory. Following such a reference makes
    /// KillMutants mutate the generator itself, and every one of those mutants is uncoverable - the
    /// tests do not execute that code, and there is no assembly in the output directory to swap.
    /// </para>
    /// <para>
    /// Measured on the generator fixture: ten of its twelve mutants came from the generator's own
    /// source and dragged the score from 100% to 16.67%, against a project whose tests are perfectly
    /// good.
    /// </para>
    /// </remarks>
    private static bool RunsAtRunTime(JsonElement reference) =>
        !Metadata(reference, "OutputItemType").Equals("Analyzer", StringComparison.OrdinalIgnoreCase) &&
        !Metadata(reference, "ReferenceOutputAssembly").Equals("false", StringComparison.OrdinalIgnoreCase);

    private static string Metadata(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>
    /// Every file a project compiles or carries, as absolute paths, read from evaluation alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The authoritative answer to "does this project own this file", which the directory it sits in
    /// only approximates. A project can compile a file from anywhere - a <c>Compile</c> item with a
    /// <c>Link</c>, a glob reaching out of the project folder - and review found the consequence: a
    /// test project including <c>../SharedTests/Assertions.cs</c> made a change to that file look
    /// like production code, so deleting an assertion from it produced an empty, passing partial run.
    /// </para>
    /// <para>
    /// <c>None</c> and <c>Content</c> come along because a test project's inputs are not only its
    /// source: a fixture file, a JSON case list and an <c>appsettings</c> are all things a change to
    /// which can stop a test reaching a mutant.
    /// </para>
    /// <para>
    /// Evaluation only, so no build and no restore is needed - the same property the project graph
    /// query relies on. It is asked once per test project and only by a partial run, so a full run
    /// pays nothing for it.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetInputFilesAsync(
        string projectPath,
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["msbuild", projectPath, $"-p:Configuration={_configuration}", "-nologo"];

        if (!string.IsNullOrEmpty(targetFramework))
        {
            arguments.Add($"-p:TargetFramework={targetFramework}");
        }

        arguments.AddRange(["-getItem:Compile", "-getItem:None", "-getItem:Content"]);

        string output = await RunRawAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        using JsonDocument json = ParseJson(projectPath, output);

        return
        [
            .. ReadFullPaths(json.RootElement, "Compile"),
            .. ReadFullPaths(json.RootElement, "None"),
            .. ReadFullPaths(json.RootElement, "Content"),
        ];
    }

    /// <summary>
    /// Reads an item's <c>FullPath</c> metadata, which MSBuild has already resolved for us.
    /// </summary>
    /// <remarks>
    /// <c>Identity</c> is relative to the project and would have to be resolved against it, which is
    /// exactly wrong for a linked file: its identity is the <c>..</c> path that makes it interesting.
    /// </remarks>
    private static IEnumerable<string> ReadFullPaths(JsonElement root, string itemName)
    {
        if (!root.TryGetProperty("Items", out JsonElement items) ||
            !items.TryGetProperty(itemName, out JsonElement values))
        {
            return [];
        }

        return values.EnumerateArray()
            .Select(item =>
                item.TryGetProperty("FullPath", out JsonElement path) ? path.GetString() : null)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => Path.GetFullPath(path!));
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

        string output = await RunRawAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        // MSBuild prints the bare value when a single property is requested, and a JSON document
        // when several are. Both shapes are real; neither is a fallback for the other.
        if (propertyNames.Count == 1)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [propertyNames[0]] = output.Trim(),
            };
        }

        using JsonDocument json = ParseJson(projectPath, output);

        if (!json.RootElement.TryGetProperty("Properties", out JsonElement properties))
        {
            throw new ProjectAnalysisException($"MSBuild returned no properties for '{projectPath}'.");
        }

        return properties.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>
    /// Obtains the exact <c>csc</c> command line MSBuild would invoke for this project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SkipCompilerExecution</c> stops csc from actually running, and
    /// <c>ProvideCommandLineArgs</c> makes MSBuild publish the arguments it would have passed.
    /// </para>
    /// <para>
    /// Two tempting extra switches are deliberately absent, both learned the hard way on a
    /// multi-project solution.
    /// </para>
    /// <para>
    /// Redirecting <c>IntermediateOutputPath</c> to isolate the query from the user's <c>obj</c>
    /// directory is wrong: the property is global, so it propagates to every referenced project,
    /// and the command line then points at reference assemblies in a directory where the compiler
    /// was never allowed to run.
    /// </para>
    /// <para>
    /// Passing <c>CopyBuildOutputToOutputDirectory=false</c> is worse: nothing is copied to the
    /// output directory, MSBuild's incremental clean then sees an assembly it did not write, and
    /// <em>deletes the built assembly from <c>bin</c></em>. The next project's query fails trying to
    /// copy the reference that just vanished. Leaving the copy enabled is safe precisely because
    /// this query runs after a real build: the assembly in <c>obj</c> still exists, so the copy
    /// succeeds and nothing is cleaned. This query must therefore never be the first thing to touch
    /// a project.
    /// </para>
    /// <para>
    /// If MSBuild decides the project is up to date it skips <c>CoreCompile</c> and returns nothing
    /// at all - reliably so for a project that has just been built, which is exactly our situation.
    /// <see cref="Analysis.CscCommandLine.Parse"/> would then happily produce an empty compilation,
    /// so the target is forced to run and the result validated as well.
    /// </para>
    /// <para>
    /// <paramref name="targetFramework"/> is not optional in practice, and leaving it out was a
    /// defect. A multi-targeted project compiles in its inner builds, never in the outer one, so an
    /// unqualified query returns an empty list: measured against the .NET 10 SDK, an outer build of
    /// a project already built for both frameworks answers <c>"CscCommandLineArgs": []</c> and exits
    /// zero. <see cref="Analysis.CscCommandLine.Parse"/> then refuses it - correctly, but blaming a
    /// project that was built perfectly well - and the run stops rather than mutating a library that
    /// KillMutants is meant to support.
    /// </para>
    /// </remarks>
    /// <param name="projectPath">The project to ask about.</param>
    /// <param name="targetFramework">
    /// The framework to compile for. Required for a project that targets several; harmless for one
    /// that targets a single framework, which is why the caller passes it unconditionally.
    /// </param>
    /// <param name="cancellationToken">Cancels the MSBuild invocation.</param>
    public async Task<IReadOnlyList<string>> GetCscCommandLineAsync(
        string projectPath,
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        ForceCompileToRun(projectPath);

        List<string> arguments =
        [
            "build", projectPath,
            $"-p:Configuration={_configuration}",
            "-t:Build",
            "-p:ProvideCommandLineArgs=true",
            "-p:SkipCompilerExecution=true",
            "-nologo",
        ];

        if (!string.IsNullOrEmpty(targetFramework))
        {
            arguments.Add($"-p:TargetFramework={targetFramework}");
        }

        arguments.Add("-getItem:CscCommandLineArgs");

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

    private static async Task<JsonDocument> RunAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string output = await RunRawAsync(projectPath, arguments, cancellationToken).ConfigureAwait(false);

        return ParseJson(projectPath, output);
    }

    private static JsonDocument ParseJson(string projectPath, string output)
    {
        try
        {
            return JsonDocument.Parse(output);
        }
        catch (JsonException exception)
        {
            throw new ProjectAnalysisException(
                $"Could not read MSBuild's output for '{projectPath}'.{Environment.NewLine}{output}",
                exception);
        }
    }

    private static async Task<string> RunRawAsync(
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

        return result.StandardOutput;
    }
}
