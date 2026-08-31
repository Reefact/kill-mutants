using System.Runtime.CompilerServices;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A throwaway copy of the sample fixture, so a test can change the code under test or its tests
/// without touching the repository's own fixture.
/// </summary>
internal sealed class FixtureCopy : IDisposable
{
    private FixtureCopy(string root) => Root = root;

    /// <summary>The copied fixture's root directory.</summary>
    public string Root { get; }

    /// <summary>The copied test source file.</summary>
    public string TestSourceFile => Path.Combine(Root, "Sample.Library.Tests", "AgesTests.cs");

    /// <summary>Copies the sample fixture into a fresh temporary directory.</summary>
    public static FixtureCopy Create()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"killmutants-e2e-{Guid.NewGuid():N}");

        CopyDirectory(SourceFixtureDirectory, destination);

        return new FixtureCopy(destination);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>The repository's own fixture, resolved from this file's path.</summary>
    public static string SourceFixtureDirectory { get; } = ResolveFixtureDirectory();

    private static string ResolveFixtureDirectory([CallerFilePath] string sourceFilePath = "")
    {
        // <root>/tests/KillMutants.EndToEnd.Tests/FixtureCopy.cs
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

        return Path.Combine(repositoryRoot, "tests", "fixtures");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);

            // Build output would be stale and is rebuilt anyway; copying it wastes seconds.
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }
}
