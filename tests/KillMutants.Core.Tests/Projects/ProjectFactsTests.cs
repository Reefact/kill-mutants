using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// What makes a project a test project, and what does not.
/// </summary>
/// <remarks>
/// <para>
/// The rule used to be "references a package starting with xunit.v3", which is true of
/// <c>xunit.v3.assert</c> and <c>xunit.v3.extensibility.core</c> - the packages xUnit directs a
/// helper library to, since it refuses to be referenced by a non-executable project at all. A
/// library of builders was therefore classified as a test suite, the run tried to launch it, and it
/// stopped on a missing <c>xunit.v3.core.dll</c>.
/// </para>
/// <para>
/// Requiring <c>Exe</c> is xUnit's own rule and it fixes that case, but on its own it leaves the
/// mirror case standing: an executable referencing <c>xunit.v3.extensibility.core</c> is not a test
/// project either, and calling it one makes it a wall in the reference graph. So the package is
/// checked by name, and xUnit's own <c>XunitTestProject</c> property is asked first. Both halves are
/// pinned here.
/// </para>
/// </remarks>
public class ProjectFactsTests
{
    [Theory]
    [InlineData("xunit.v3")]
    [InlineData("xunit.v3.mtp-v2")]
    [InlineData("xunit.v3.core")]
    [InlineData("xunit.v3.core.mtp-v2")]
    public void An_executable_referencing_a_test_application_package_is_a_test_project(string package)
    {
        Assert.True(Facts(outputType: "Exe", package).IsTestProject);
    }

    /// <summary>
    /// The mirror of the case that started this: same output type, same package family, and still
    /// not a test project.
    /// </summary>
    /// <remarks>
    /// Measured against the 4.0.0 packages. An executable with a <c>Main</c> of its own referencing
    /// <c>xunit.v3</c>, <c>xunit.v3.core</c> or <c>xunit.v3.mtp-v2</c> fails to build with CS0017,
    /// "Program has more than one entry point defined": xUnit had already contributed one. The same
    /// executable referencing <c>xunit.v3.assert</c> or <c>xunit.v3.extensibility.core</c> builds
    /// cleanly, because neither contributes an entry point - and the second of the two even puts
    /// <c>xunit.v3.core.dll</c> in the output directory, so it passes the runnability check this
    /// tool makes after the build as well. Nothing downstream would have caught it.
    /// </remarks>
    [Theory]
    [InlineData("xunit.v3.assert")]
    [InlineData("xunit.v3.extensibility.core")]
    [InlineData("xunit.v3.common")]
    public void An_executable_referencing_only_a_library_package_is_not(string package)
    {
        Assert.False(Facts(outputType: "Exe", package).IsTestProject);
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

    /// <summary>
    /// xUnit's own answer wins over the package list, which is the point of asking it.
    /// </summary>
    /// <remarks>
    /// <c>XunitTestProject</c> is set in the <c>buildTransitive</c> props of
    /// <c>xunit.v3.core.mtp-v2</c>, so it is true for any flavour of the package family, including
    /// ones this tool has never heard of. It arrives through NuGet's generated imports and is
    /// therefore only there once the project has been restored - which is why it cannot be the only
    /// question asked.
    /// </remarks>
    [Fact]
    public void A_project_xunit_itself_calls_a_test_project_is_one()
    {
        Assert.True(Facts(outputType: "Exe", "xunit.v3.some.future.flavour", xunitTestProject: true)
            .IsTestProject);
    }

    /// <summary>
    /// The property is xUnit's answer to "is this a test project", not to "may this be launched".
    /// </summary>
    /// <remarks>
    /// It cannot in practice be true on a library, because xUnit fails the build first. Requiring
    /// <c>Exe</c> anyway costs nothing and keeps the two conditions independent, so a project that
    /// somehow sets the property by hand is still not launched as a test application.
    /// </remarks>
    [Fact]
    public void A_library_is_not_a_test_project_whatever_it_declares()
    {
        Assert.False(Facts(outputType: "Library", "xunit.v3", xunitTestProject: true).IsTestProject);
    }

    /// <summary>MSBuild booleans arrive in whatever case the project author wrote them.</summary>
    [Fact]
    public void The_output_type_is_compared_without_regard_to_case()
    {
        Assert.True(Facts(outputType: "exe", "xunit.v3").IsTestProject);
    }

    /// <summary>And so do package identities, which NuGet itself compares without case.</summary>
    [Fact]
    public void The_package_is_compared_without_regard_to_case()
    {
        Assert.True(Facts(outputType: "Exe", "XUnit.V3.MTP-V2").IsTestProject);
    }

    /// <summary>
    /// A declaration outranks detection, so the two answers can never both be yes.
    /// </summary>
    /// <remarks>
    /// Review found this. <c>ProjectDiscovery</c> reaches the test-project check first and stops
    /// walking there, so a declared helper that happens to be a runnable test application would be
    /// launched as a suite and would hide everything it references - the declaration silently
    /// ignored. It is a statement of what the project is for, and no structural fact outranks that.
    /// </remarks>
    [Fact]
    public void A_declared_support_project_is_not_a_test_project_however_it_is_built()
    {
        ProjectFacts facts = Facts(
            outputType: "Exe", "xunit.v3.mtp-v2", declaredTestSupport: true, xunitTestProject: true);

        Assert.True(facts.IsTestSupport);
        Assert.False(facts.IsTestProject);
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
        bool declaredTestSupport = false,
        bool xunitTestProject = false) =>
        new(
            ProjectPath: "/repo/Sample/Sample.csproj",
            AssemblyFileName: "Sample.dll",
            AssemblyPath: "/repo/Sample/bin/Sample.dll",
            OutputDirectory: "/repo/Sample/bin",
            TargetFramework: "net10.0",
            TargetFrameworks: [],
            OutputType: outputType,
            XunitTestProject: xunitTestProject,
            DeclaredTestSupport: declaredTestSupport,
            PackageReferences: [package],
            ProjectReferences: [],
            InputFiles: []);
}
