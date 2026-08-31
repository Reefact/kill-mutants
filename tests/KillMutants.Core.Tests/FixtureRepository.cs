using System.Runtime.CompilerServices;

namespace KillMutants.Core.Tests;

/// <summary>Locates the real fixture projects the integration tests run against.</summary>
internal static class FixtureRepository
{
    /// <summary>The directory holding <c>Sample.Library</c> and <c>Sample.Library.Tests</c>.</summary>
    public static string SampleDirectory { get; } = Path.Combine(RepositoryRoot(), "tests", "fixtures", "single");

    public static string SampleLibraryProject { get; } =
        Path.Combine(SampleDirectory, "Sample.Library", "Sample.Library.csproj");

    /// <summary>
    /// Resolved from this source file's own path rather than from the test assembly's location, so
    /// the tests keep working regardless of configuration or output layout.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        // <root>/tests/KillMutants.Core.Tests/FixtureRepository.cs
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
    }
}
