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

    /// <summary>
    /// Sets <c>UseMicrosoftTestingPlatformRunner</c> on the copied test project, which inverts the
    /// entry point xUnit generates so that the test application defaults to the Microsoft Testing
    /// Platform host instead of xUnit's console runner.
    /// </summary>
    public void UseMicrosoftTestingPlatformRunner()
    {
        string project = Path.Combine(Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");
        string content = File.ReadAllText(project);

        File.WriteAllText(
            project,
            content.Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>" +
                Environment.NewLine +
                "    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Replaces the fixture's code with a loop whose mutation never terminates, and the tests that
    /// cover it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutating <c>value = value + 1</c> into <c>value = value - 1</c> makes the loop condition
    /// permanently true. No dedicated mutator is needed for this: the arithmetic family already
    /// reaches it, which is precisely why the deadline has to exist before the catalogue grows.
    /// The fixture is replaced rather than extended so this test runs four mutants instead of
    /// thirteen.
    /// </para>
    /// <para>
    /// The counters are <c>long</c> on purpose, and the first attempt at this fixture got it wrong.
    /// With <c>int</c>, the decrementing counter reaches <c>int.MinValue</c>, wraps to
    /// <c>int.MaxValue</c>, and the condition goes false: the loop finishes after about two billion
    /// iterations and the mutant is reported killed rather than timed out. Widening to
    /// <c>long</c> puts the wrap around nine quintillion iterations away, which is endless by any
    /// measure that matters.
    /// </para>
    /// </remarks>
    public void UseCodeWhoseMutationNeverTerminates()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Ages.cs"),
            """
            namespace Sample.Library;

            public static class Sums
            {
                public static long UpTo(long limit)
                {
                    long total = 0;
                    long value = 1;

                    while (value <= limit)
                    {
                        total = total + value;
                        value = value + 1;
                    }

                    return total;
                }
            }

            """);

        File.WriteAllText(
            TestSourceFile,
            """
            using Sample.Library;

            namespace Sample.Library.Tests;

            public class SumsTests
            {
                [Fact]
                public void Sums_every_number_up_to_the_limit()
                {
                    Assert.Equal(6L, Sums.UpTo(3));
                }
            }

            """);
    }

    /// <summary>
    /// Replaces the fixture's code with a type that depends on a source generator.
    /// </summary>
    /// <remarks>
    /// <c>[GeneratedRegex]</c> stands in for the whole family — <c>[JsonSerializable]</c>,
    /// <c>[LibraryImport]</c>, Mapperly, Refit, ASP.NET Core minimal APIs. MSBuild names generators
    /// on the compiler command line under <c>/analyzer:</c> but does not list the code they
    /// contribute, so a tool that only reads the source list cannot compile the project at all: the
    /// partial property has no implementation and the emit fails with CS9248.
    /// </remarks>
    public void UseCodeThatDependsOnASourceGenerator()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Ages.cs"),
            """
            using System.Text.RegularExpressions;

            namespace Sample.Library;

            public static partial class Codes
            {
                [GeneratedRegex(@"^[A-Z]{2}\d{3}$")]
                private static partial Regex Pattern { get; }

                public static bool IsValid(string code)
                {
                    return code.Length >= 5 && Pattern.IsMatch(code);
                }
            }

            """);

        File.WriteAllText(
            TestSourceFile,
            """
            using Sample.Library;

            namespace Sample.Library.Tests;

            public class CodesTests
            {
                [Theory]
                [InlineData("AB123", true)]
                [InlineData("AB12", false)]
                [InlineData("ABC12", false)]
                public void A_code_is_two_letters_then_three_digits(string code, bool expected)
                {
                    Assert.Equal(expected, Codes.IsValid(code));
                }
            }

            """);
    }

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
