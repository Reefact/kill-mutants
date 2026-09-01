using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// KillMutants states "xUnit 4 only" as a contract, so it has to check rather than announce. The
/// three answers are kept apart on purpose: supported, an earlier version named in the message, and
/// a version that could not be read at all.
/// </summary>
public class XUnitVersionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Read from the real assembly this test project runs on, which is the whole point of reading the
    /// output directory rather than the project file: this project uses Central Package Management,
    /// where the PackageReference item carries no version at all.
    /// </summary>
    [Fact]
    public void The_version_comes_from_the_assembly_that_will_actually_be_loaded()
    {
        Version? version = XUnitVersion.In(AppContext.BaseDirectory);

        Assert.NotNull(version);
        Assert.Equal(XUnitVersion.Supported, version.Major);
        Assert.Null(XUnitVersion.WhyUnsupported(AppContext.BaseDirectory));
    }

    [Fact]
    public void An_output_directory_without_the_core_assembly_cannot_be_confirmed()
    {
        Assert.Null(XUnitVersion.In(_directory));

        string? reason = XUnitVersion.WhyUnsupported(_directory);

        Assert.NotNull(reason);
        Assert.Contains("could not find", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file that is not an assembly at all is the same answer as a missing one - unknown - rather
    /// than an exception escaping into the middle of a run.
    /// </summary>
    [Fact]
    public void Something_that_is_not_an_assembly_cannot_be_confirmed_either()
    {
        File.WriteAllText(Path.Combine(_directory, "xunit.v3.core.dll"), "not an assembly");

        Assert.Null(XUnitVersion.In(_directory));
        Assert.NotNull(XUnitVersion.WhyUnsupported(_directory));
    }

    [Fact]
    public void The_supported_version_is_the_one_the_documentation_claims()
    {
        // If this ever changes, README and the architecture document have to change with it.
        Assert.Equal(4, XUnitVersion.Supported);
    }
}
