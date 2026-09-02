using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// What makes a project a test project, and what does not.
/// </summary>
/// <remarks>
/// The rule used to be "references a package starting with xunit.v3", which is true of
/// <c>xunit.v3.assert</c> and <c>xunit.v3.extensibility.core</c> - the packages xUnit directs a
/// helper library to, since it refuses to be referenced by a non-executable project at all. A
/// library of builders was therefore classified as a test suite, the run tried to launch it, and it
/// stopped on a missing <c>xunit.v3.core.dll</c>. Requiring <c>Exe</c> is xUnit's own rule, not a
/// patch invented for the symptom.
/// </remarks>
public class ProjectFactsTests
{
    [Theory]
    [InlineData("xunit.v3")]
    [InlineData("xunit.v3.mtp-v2")]
    [InlineData("xunit.v3.core")]
    public void An_executable_project_referencing_xunit_is_a_test_project(string package)
    {
        Assert.True(Facts(outputType: "Exe", package).IsTestProject);
    }

    [Theory]
    [InlineData("xunit.v3.assert")]
    [InlineData("xunit.v3.extensibility.core")]
    public void A_library_referencing_xunit_is_test_support_at_most(string package)
    {
        Assert.False(Facts(outputType: "Library", package).IsTestProject);
    }

    /// <summary>
    /// The package matters as much as the output type: an executable that runs no tests is not a
    /// test project either, however it is named.
    /// </summary>
    [Fact]
    public void An_executable_referencing_no_test_framework_is_not_one()
    {
        Assert.False(Facts(outputType: "Exe", "Serilog").IsTestProject);
    }

    /// <summary>MSBuild booleans arrive in whatever case the project author wrote them.</summary>
    [Fact]
    public void The_output_type_is_compared_without_regard_to_case()
    {
        Assert.True(Facts(outputType: "exe", "xunit.v3").IsTestProject);
    }

    [Fact]
    public void A_project_is_test_support_only_when_it_says_so()
    {
        Assert.False(Facts(outputType: "Library", "Serilog").IsTestSupport);
        Assert.True(Facts(outputType: "Library", "Serilog", declaredTestSupport: true).IsTestSupport);
    }

    private static ProjectFacts Facts(
        string outputType,
        string package,
        bool declaredTestSupport = false) =>
        new(
            ProjectPath: "/repo/Sample/Sample.csproj",
            AssemblyFileName: "Sample.dll",
            AssemblyPath: "/repo/Sample/bin/Sample.dll",
            OutputDirectory: "/repo/Sample/bin",
            TargetFramework: "net10.0",
            TargetFrameworks: [],
            OutputType: outputType,
            DeclaredTestSupport: declaredTestSupport,
            PackageReferences: [package],
            ProjectReferences: []);
}
