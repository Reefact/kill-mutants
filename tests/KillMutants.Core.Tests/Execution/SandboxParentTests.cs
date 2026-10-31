using KillMutants.Execution;
using KillMutants.Projects;

namespace KillMutants.Core.Tests.Execution;

/// <summary>
/// Regression tests (#45, RB-032). A sandbox in the temporary directory had none of the ancestors
/// the test output has, and a test that walks up to find the repository failed the baseline.
/// </summary>
public class SandboxParentTests
{
    [Theory]
    [InlineData("/repo/Sample.Tests/bin/Release/net10.0/", "/repo/Sample.Tests/bin/Release/killmutants")]
    [InlineData("/repo/Sample.Tests/bin/Release/net10.0", "/repo/Sample.Tests/bin/Release/killmutants")]
    [InlineData("/repo/artifacts/bin/Sample.Tests/release/", "/repo/artifacts/bin/Sample.Tests/killmutants")]
    public void The_sandboxes_sit_beside_the_first_test_projects_build_output(string output, string expected)
    {
        var target = new MutationTestTarget(
            new ProjectUnderTest("/repo/Sample/Sample.csproj", "/repo/Sample", "Sample.dll", "net10.0"),
            [new TestProject("/repo/Sample.Tests/Sample.Tests.csproj", Path.Combine(output, "Sample.Tests.dll"), output)]);

        Assert.Equal(Path.GetFullPath(expected), MutationTestSession.SandboxParent(target));
    }

    /// <summary>
    /// What the move is for: from inside the sandbox, walking up still reaches the repository the
    /// test output was built in.
    /// </summary>
    [Fact]
    public void Walking_up_from_the_sandbox_reaches_the_repository()
    {
        const string Output = "/repo/Sample.Tests/bin/Release/net10.0";
        var target = new MutationTestTarget(
            new ProjectUnderTest("/repo/Sample/Sample.csproj", "/repo/Sample", "Sample.dll", "net10.0"),
            [new TestProject("/repo/Sample.Tests/Sample.Tests.csproj", $"{Output}/Sample.Tests.dll", Output)]);

        string copy = Path.Combine(MutationTestSession.SandboxParent(target), "some-run", "0", "Sample.Tests-0");

        Assert.Contains(
            Path.GetFullPath("/repo"),
            Ancestors(copy));
    }

    private static List<string> Ancestors(string path)
    {
        List<string> found = [];

        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            found.Add(directory.FullName);
        }

        return found;
    }
}
