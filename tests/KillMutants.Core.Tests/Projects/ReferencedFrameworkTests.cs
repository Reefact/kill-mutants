using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// Which build of a library a test project loads, read from what NuGet decided rather than guessed.
/// </summary>
/// <remarks>
/// A <c>net10.0</c> suite over a library built for <c>netstandard2.0;netstandard2.1</c> loads
/// <c>netstandard2.1</c>. The library cannot be asked about the suite's own framework, and the
/// nearest-framework rule that picks the answer is NuGet's - these tests hand it a restore result
/// and check that the verdict in it is the one used, and that a library with no choice never needs it.
/// </remarks>
public sealed class ReferencedFrameworkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "km-rf-" + Guid.NewGuid().ToString("N"));

    public ReferencedFrameworkTests() => Directory.CreateDirectory(Path.Combine(_root, "Tests", "obj"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(".NETStandard,Version=v2.0", "netstandard2.0")]
    [InlineData(".NETStandard,Version=v2.1", "netstandard2.1")]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0")]
    [InlineData(".NETCoreApp,Version=v3.1", "netcoreapp3.1")]
    [InlineData(".NETFramework,Version=v4.7.2", "net472")]
    [InlineData(".NETFramework,Version=v4.8", "net48")]
    [InlineData("Nonsense", null)]
    public void A_framework_name_is_spelled_the_way_a_project_file_spells_it(string framework, string? moniker)
    {
        Assert.Equal(moniker, ReferencedFramework.ToMoniker(framework));
    }

    [Fact]
    public void The_framework_NuGet_chose_among_those_the_library_offers_is_the_one_returned()
    {
        ProjectFacts tests = Facts("Tests", "net10.0", assets: WriteAssets(".NETStandard,Version=v2.1"));
        ProjectFacts library = Facts("Lib", frameworks: ["netstandard2.0", "netstandard2.1"]);

        Assert.Equal("netstandard2.1", ReferencedFramework.For(tests, library));
    }

    [Fact]
    public void A_library_with_one_framework_needs_no_restore_result_to_name_it()
    {
        ProjectFacts tests = Facts("Tests", "net10.0", assets: string.Empty);
        ProjectFacts library = Facts("Lib", framework: "netstandard2.0");

        Assert.Equal("netstandard2.0", ReferencedFramework.For(tests, library));
    }

    [Fact]
    public void A_library_with_a_choice_and_no_restore_result_is_left_for_the_caller_to_decide()
    {
        ProjectFacts tests = Facts("Tests", "net10.0", assets: Path.Combine(_root, "Tests", "obj", "missing.json"));
        ProjectFacts library = Facts("Lib", frameworks: ["netstandard2.0", "netstandard2.1"]);

        Assert.Null(ReferencedFramework.For(tests, library));
    }

    [Fact]
    public void A_library_declaring_the_suites_own_framework_is_recognised_without_reading_anything()
    {
        ProjectFacts library = Facts("Lib", frameworks: ["netstandard2.0", "net10.0"]);

        Assert.True(ReferencedFramework.Declares(library, "net10.0"));
        Assert.False(ReferencedFramework.Declares(library, "net8.0"));
    }

    [Theory]
    [InlineData("net10.0-windows", "net10.0-windows7.0", "net10.0-windows")]
    [InlineData("net10.0", "net10.0", "net10.0")]
    [InlineData("net10.0-android", "net10.0-android36.0", "net10.0")]
    public void A_platform_build_is_chosen_only_for_a_suite_on_that_platform(
        string suite, string target, string expected)
    {
        ProjectFacts tests = Facts("Tests", suite, assets: WriteAssets(".NETCoreApp,Version=v10.0", target));
        ProjectFacts library = Facts("Lib", frameworks: ["net10.0", "net10.0-windows"]);

        // The restore result names net10.0 for all three: it never carries the platform.
        Assert.Equal(expected, ReferencedFramework.For(tests, library));
    }

    [Fact]
    public void Two_builds_that_cannot_be_told_apart_leave_the_choice_to_the_caller()
    {
        ProjectFacts tests = Facts("Tests", "net10.0-ios", assets: WriteAssets(".NETCoreApp,Version=v10.0", "net10.0-ios18.0"));
        ProjectFacts library = Facts("Lib", frameworks: ["net10.0-windows", "net10.0-android"]);

        Assert.Null(ReferencedFramework.For(tests, library));
    }

    [Fact]
    public void A_library_written_with_the_plural_property_and_one_entry_still_names_its_framework()
    {
        ProjectFacts tests = Facts("Tests", "net10.0", assets: string.Empty);
        ProjectFacts library = Facts("Lib", frameworks: ["netstandard2.0"]) with { TargetFramework = string.Empty };

        Assert.Equal("netstandard2.0", ReferencedFramework.For(tests, library));
        Assert.True(ReferencedFramework.Declares(library, "netstandard2.0"));
        Assert.False(ReferencedFramework.Declares(library, "net10.0"));
    }

    private string WriteAssets(string libraryFramework, string target = "net10.0")
    {
        string path = Path.Combine(_root, "Tests", "obj", "project.assets.json");

        File.WriteAllText(path, $$"""
            {
              "version": 3,
              "targets": { "{{target}}": { "Lib/1.0.0": { "type": "project", "framework": "{{libraryFramework}}" } } },
              "libraries": { "Lib/1.0.0": { "type": "project", "path": "../Lib/Lib.csproj" } }
            }
            """);

        return path;
    }

    private ProjectFacts Facts(
        string name, string framework = "", string assets = "", string[]? frameworks = null) =>
        new(
            ProjectPath: Path.Combine(_root, name, name + ".csproj"),
            AssemblyFileName: name + ".dll",
            AssemblyPath: Path.Combine(_root, name, "bin", name + ".dll"),
            OutputDirectory: Path.Combine(_root, name, "bin"),
            TargetFramework: frameworks is null ? framework : string.Empty,
            TargetFrameworks: frameworks ?? [],
            OutputType: "Library",
            XunitTestProject: false,
            DeclaredTestSupport: false,
            ProjectAssetsFile: assets,
            PackageReferences: [],
            ProjectReferences: [],
            AnalyzerProjects: [],
            InputFiles: []);
}
