using KillMutants.Execution;
using KillMutants.Projects;

namespace KillMutants.Core.Tests.Execution;

/// <summary>
/// Regression tests (#45, RB-032). A sandbox in the temporary directory had none of the ancestors
/// the test output has, and a test that walks up to find the repository failed the baseline.
/// </summary>
public sealed class SandboxParentTests : IDisposable
{
    private readonly string _repository = Directory.CreateTempSubdirectory("killmutants-repo-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repository, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Theory]
    [InlineData("/repo/Sample.Tests/bin/Release/net10.0/", "/repo/Sample.Tests/bin/Release/killmutants")]
    [InlineData("/repo/Sample.Tests/bin/Release/net10.0", "/repo/Sample.Tests/bin/Release/killmutants")]
    [InlineData("/repo/artifacts/bin/Sample.Tests/release/", "/repo/artifacts/bin/Sample.Tests/killmutants")]
    public void A_test_projects_copies_sit_beside_its_own_build_output(string output, string expected)
    {
        var testProject = new TestProject(
            "/repo/Sample.Tests/Sample.Tests.csproj", Path.Combine(output, "Sample.Tests.dll"), output);

        Assert.Equal(Path.GetFullPath(expected), MutationTestSession.SandboxParentOf(testProject));
    }

    /// <summary>
    /// Regression test (review of #58). A target may hold several suites in different directories;
    /// each one's copy must keep its own ancestors, not the first suite's.
    /// </summary>
    [Fact]
    public void Each_suite_of_a_target_keeps_its_own_ancestors()
    {
        TestProject first = Suite("TestsA");
        TestProject second = Suite("TestsB");
        var target = new MutationTestTarget(
            new ProjectUnderTest(Path.Combine(_repository, "Lib", "Lib.csproj"), Path.Combine(_repository, "Lib"), "Lib.dll", "net10.0"),
            [first, second]);

        (Dictionary<string, string> runRoots, string[] projectRoots) = MutationTestSession.CreateRunRoots(target);
        using TestSandbox sandbox = TestSandbox.CreateFor(
            target, Path.Combine(projectRoots[0], "0"), [.. projectRoots.Select(root => Path.Combine(root, "0"))]);

        Assert.Equal(2, runRoots.Count);
        Assert.Contains(Path.Combine(_repository, "TestsA"), Ancestors(sandbox.TestProjects[0].OutputDirectory));
        Assert.Contains(Path.Combine(_repository, "TestsB"), Ancestors(sandbox.TestProjects[1].OutputDirectory));
        Assert.DoesNotContain(Path.Combine(_repository, "TestsA"), Ancestors(sandbox.TestProjects[1].OutputDirectory));
    }

    private TestProject Suite(string name)
    {
        string output = Path.Combine(_repository, name, "bin", "Release", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"{name}.dll"), "x");
        File.WriteAllText(Path.Combine(output, "Lib.dll"), "x");

        return new TestProject(Path.Combine(_repository, name, $"{name}.csproj"), Path.Combine(output, $"{name}.dll"), output);
    }

    private static List<string> Ancestors(string path)
    {
        List<string> found = [];

        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            found.Add(directory.FullName.TrimEnd(Path.DirectorySeparatorChar));
        }

        return found;
    }
}
