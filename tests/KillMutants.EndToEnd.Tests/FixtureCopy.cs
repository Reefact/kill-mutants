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

    /// <summary>
    /// Adds a method whose tests are real but incomplete, so the run scores below 100% with genuine
    /// survivors rather than with mutants that could not be tested.
    /// </summary>
    public void AddPartlyTestedCode()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Discounts.cs"),
            """
            namespace Sample.Library;

            public static class Discounts
            {
                public static bool Applies(int quantity, bool member)
                {
                    return quantity >= 10 && member;
                }
            }

            """);

        File.AppendAllText(
            TestSourceFile,
            """

            public class DiscountsTests
            {
                // Only the comfortable case: neither the boundary nor the member flag is probed, so
                // several mutants of Applies survive.
                [Fact]
                public void A_large_order_from_a_member_gets_the_discount()
                {
                    Assert.True(Discounts.Applies(50, true));
                }
            }

            """);
    }

    /// <summary>Adds a method to the library that no test exercises.</summary>
    public void AddCodeNoTestReaches()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Untested.cs"),
            """
            namespace Sample.Library;

            public static class Untested
            {
                public static bool NobodyCallsThis(int value)
                {
                    return value >= 100;
                }
            }

            """);
    }

    /// <summary>
    /// Adds a type whose value is computed once, in a static initialiser, and read by two tests.
    /// </summary>
    /// <remarks>
    /// Stryker.NET marks such mutants <c>IsStaticValue</c> and runs them against every test that is
    /// not trusted to miss them, because a static initialiser runs once per process and their
    /// coverage pass shares one. This fixture is how we check that the same trap does not exist
    /// here - see the test that uses it.
    /// </remarks>
    public void AddCodeBehindAStaticInitialiser()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Thresholds.cs"),
            """
            namespace Sample.Library;

            public static class Thresholds
            {
                // Evaluated once, the first time anything in this class is touched.
                public static readonly int Adult = 9 * 2;

                public static bool IsAdult(int age)
                {
                    return age >= Adult;
                }

                public static bool IsMinor(int age)
                {
                    return age < Adult;
                }
            }

            """);

        File.WriteAllText(
            Path.Combine(Root, "Sample.Library.Tests", "ThresholdsTests.cs"),
            """
            using Sample.Library;

            namespace Sample.Library.Tests;

            public class ThresholdsTests
            {
                // Two tests in two different classes, each reaching the static value through a
                // different method, so neither can be the only one credited with covering it.
                [Fact]
                public void Adulthood_starts_at_eighteen()
                {
                    Assert.True(Thresholds.IsAdult(18));
                    Assert.False(Thresholds.IsAdult(17));
                }

                [Fact]
                public void Minority_ends_at_eighteen()
                {
                    Assert.True(Thresholds.IsMinor(17));
                    Assert.False(Thresholds.IsMinor(18));
                }
            }

            """);
    }

    /// <summary>
    /// Adds named thresholds: one the tests assert at, one they assert only around, and one only
    /// the tests read.
    /// </summary>
    /// <remarks>
    /// The three cases DEC0019 is about. The constants the library reads are recompiled with each
    /// mutant, so its own comparisons see the new value; the one only the tests read is copied into
    /// the test assembly, which keeps the original whatever the mutant says.
    /// </remarks>
    public void AddNamedThresholds()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Limits.cs"),
            """
            namespace Sample.Library;

            public static class Limits
            {
                public const int MinimumNouns = 100;

                public const int MaximumLength = 40;

                public const int ReadByTheTestsOnly = 7;

                public static bool HasEnoughNouns(int nouns)
                {
                    return nouns >= MinimumNouns;
                }

                public static bool FitsInASlug(int length)
                {
                    return length <= MaximumLength;
                }
            }

            """);

        File.WriteAllText(
            Path.Combine(Root, "Sample.Library.Tests", "LimitsTests.cs"),
            """
            using Sample.Library;

            namespace Sample.Library.Tests;

            public class LimitsTests
            {
                // At the boundary: both neighbours of 100 change one of these answers.
                [Fact]
                public void A_hundred_nouns_is_enough_and_ninety_nine_is_not()
                {
                    Assert.True(Limits.HasEnoughNouns(100));
                    Assert.False(Limits.HasEnoughNouns(99));
                }

                // Around it only: 39 and 41 give the same answers as 40.
                [Fact]
                public void A_short_slug_fits_and_a_huge_one_does_not()
                {
                    Assert.True(Limits.FitsInASlug(5));
                    Assert.False(Limits.FitsInASlug(500));
                }

                [Fact]
                public void The_constant_only_the_tests_read_is_seven()
                {
                    Assert.Equal(7, Limits.ReadByTheTestsOnly);
                }
            }

            """);
    }

    /// <summary>
    /// Adds a second generator to the copied generator fixture, one that throws, and whose output
    /// nothing in the library needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves matter. A broken generator whose code the library requires fails the compilation,
    /// and a failed compilation is loud. This one leaves the build working: the assembly emits, the
    /// tests pass, and nothing about the run looks wrong - which is precisely the case where a
    /// report would describe an assembly the project does not build.
    /// </para>
    /// <para>
    /// The assembly is renamed, and that is not cosmetic. Generators are loaded into
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.Default, which caches by assembly
    /// identity and not by path, so a second run in the same process gets the first run's copy of a
    /// same-named assembly - here, one carrying a generator that throws. Renaming keeps this fixture
    /// out of the way of every other test in the process. The defect itself is RB-020.
    /// </para>
    /// </remarks>
    public void AddAGeneratorThatFailsWithoutBreakingTheBuild()
    {
        string project = Path.Combine(Root, "Sample.Generator", "Sample.Generator.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>" +
                Environment.NewLine +
                "    <AssemblyName>Sample.Generator.Broken</AssemblyName>",
                StringComparison.Ordinal));

        File.WriteAllText(
            Path.Combine(Root, "Sample.Generator", "BrokenGenerator.cs"),
            """
            using Microsoft.CodeAnalysis;

            namespace Sample.Generator
            {
                [Generator]
                public class BrokenGenerator : IIncrementalGenerator
                {
                    public void Initialize(IncrementalGeneratorInitializationContext context)
                    {
                        context.RegisterPostInitializationOutput(ctx =>
                        {
                            throw new System.InvalidOperationException("this generator is broken");
                        });
                    }
                }
            }

            """);
    }

    /// <summary>
    /// Gives the copied generator fixture's generator an assembly name of its own.
    /// </summary>
    /// <remarks>
    /// The same precaution <see cref="AddAGeneratorThatFailsWithoutBreakingTheBuild"/> takes, and for
    /// the same defect. Generators are loaded into
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.Default, which caches by assembly
    /// identity rather than by path and never unloads: the second test in a process to use this
    /// fixture gets the first one's generator, compiled from a directory that no longer exists.
    /// Measured when it happened - the baseline failed with CS0103 on the type the generator
    /// contributes, and the run reported eight generators for a fixture holding two. The defect is
    /// RB-020; a distinct identity is how a test stays out of its way.
    /// </remarks>
    public void RenameTheGeneratorAssembly(string name)
    {
        string project = Path.Combine(Root, "Sample.Generator", "Sample.Generator.csproj");

        File.WriteAllText(
            project,
            File.ReadAllText(project).Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>" +
                Environment.NewLine +
                $"    <AssemblyName>{name}</AssemblyName>",
                StringComparison.Ordinal));
    }

    /// <summary>Adds a method no test calls, holding a literal nothing else in the fixture holds.</summary>
    /// <remarks>
    /// Uncovered on purpose, and identifiable on purpose: a test that wants to know what the run
    /// does with a mutant nothing reaches needs a site it can point at without ambiguity.
    /// </remarks>
    public void AddUncoveredCode()
    {
        File.WriteAllText(
            Path.Combine(Root, "Sample.Library", "Forgotten.cs"),
            """
            namespace Sample.Library;

            public static class Forgotten
            {
                public static bool IsAncient(int age)
                {
                    return age >= 4242;
                }
            }

            """);
    }

    /// <summary>Copies the single-project sample fixture into a fresh temporary directory.</summary>
    public static FixtureCopy Create() => CopyOf(SourceFixtureDirectory);

    /// <summary>
    /// Copies the multi-project fixture: two libraries and two test suites, where one library is
    /// reached by both suites and the other only through a project reference.
    /// </summary>
    public static FixtureCopy CreateMultiProject() => CopyOf(FixtureDirectory("multi"));

    /// <summary>
    /// Copies the source-generator fixture: a generator with a dependency of its own, referenced the
    /// way a packaged generator is, and a library that cannot compile without what it contributes.
    /// </summary>
    public static FixtureCopy CreateGeneratorProject() => CopyOf(FixtureDirectory("generator"));

    /// <summary>
    /// Copies the multi-targeted fixture: a library built for two frameworks, exercised by a test
    /// project that loads one of them.
    /// </summary>
    public static FixtureCopy CreateMultiTargetedProject() => CopyOf(FixtureDirectory("multitarget"));

    /// <summary>
    /// Copies the netstandard fixture: a library built for netstandard2.0 alone, exercised by a net10.0
    /// test project that therefore loads a framework the library was never asked to build for.
    /// </summary>
    public static FixtureCopy CreateNetStandardProject() => CopyOf(FixtureDirectory("netstandard"));

    /// <summary>
    /// Copies the test-support fixture: <c>Tests -&gt; Support -&gt; Library</c>, where the support
    /// project is a class library referencing <c>xunit.v3.assert</c> - the package xUnit directs a
    /// non-test library to - and carrying a mutable site of its own.
    /// </summary>
    public static FixtureCopy CreateTestSupportProject() => CopyOf(FixtureDirectory("testsupport"));

    /// <summary>Makes the support project declare itself, the way a user would.</summary>
    public void DeclareTheSupportProject()
    {
        string project = Path.Combine(Root, "Sample.Support", "Sample.Support.csproj");
        string text = File.ReadAllText(project);

        File.WriteAllText(
            project,
            text.Replace(
                "<IsPackable>false</IsPackable>",
                "<IsPackable>false</IsPackable>" + Environment.NewLine +
                "    <KillMutantsTestSupport>true</KillMutantsTestSupport>",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Copies the multi-targeted-suite fixture: a netstandard2.0 library and a test project built for
    /// <c>net8.0</c> and <c>net10.0</c>, which a run cannot choose between on its own.
    /// </summary>
    public static FixtureCopy CreateMultiTargetedSuiteProject() => CopyOf(FixtureDirectory("multitargetsuite"));

    /// <summary>
    /// Writes the suite's framework with the plural property and one entry, which is how a project
    /// that expects to gain a second one is often written. Its outer build has no
    /// <c>TargetFramework</c> any more than a project with two.
    /// </summary>
    public void WriteTheSuiteFrameworkWithThePluralProperty()
    {
        string suite = Path.Combine(Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");

        File.WriteAllText(
            suite,
            File.ReadAllText(suite).Replace(
                "<TargetFramework>net10.0</TargetFramework>",
                "<TargetFrameworks>net10.0</TargetFrameworks>",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Makes the suite reach the library only when it is built for <c>net10.0</c>, the way a project
    /// supporting two frameworks does when the second cannot use the library.
    /// </summary>
    public void ReferenceTheLibraryOnlyOnNet10()
    {
        string suite = Path.Combine(Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");

        File.WriteAllText(
            suite,
            File.ReadAllText(suite).Replace(
                """<ProjectReference Include="../Sample.Library/Sample.Library.csproj" />""",
                """<ProjectReference Include="../Sample.Library/Sample.Library.csproj" Condition="'$(TargetFramework)' == 'net10.0'" />""",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Removes the conditional reference and rewrites the suite so it still compiles, which is the
    /// change a partial run has to notice took the library's only suite away.
    /// </summary>
    public void DropTheLibraryFromTheSuite()
    {
        string suite = Path.Combine(Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj");

        File.WriteAllText(
            suite,
            File.ReadAllText(suite).Replace(
                """<ProjectReference Include="../Sample.Library/Sample.Library.csproj" Condition="'$(TargetFramework)' == 'net10.0'" />""",
                string.Empty,
                StringComparison.Ordinal));

        File.WriteAllText(
            Path.Combine(Root, "Sample.Library.Tests", "AgesTests.cs"),
            """
            namespace Sample.Library.Tests;

            public class AgesTests
            {
                [Fact]
                public void Nothing_is_asserted_about_ages_any_more() => Assert.True(true);
            }
            """);
    }

    /// <summary>
    /// Leaves the netstandard fixture restored against a library offering <c>netstandard2.0</c> and
    /// <c>netstandard2.1</c>, then changes the library to offer <c>netstandard2.0</c> and a Windows
    /// build. The suite's restore result is now stale: it names a framework the library no longer
    /// has, which is what a library edited since the last build looks like.
    /// </summary>
    public void LeaveTheSuiteRestoredAgainstAnEarlierLibrary()
    {
        string library = Path.Combine(Root, "Sample.Library", "Sample.Library.csproj");
        string text = File.ReadAllText(library);

        File.WriteAllText(
            library,
            text.Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                "<TargetFrameworks>netstandard2.0;netstandard2.1</TargetFrameworks>",
                StringComparison.Ordinal));

        Restore(Path.Combine(Root, "Sample.Library.Tests", "Sample.Library.Tests.csproj"));

        File.WriteAllText(
            library,
            text.Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                "<TargetFrameworks>netstandard2.0;net10.0-windows</TargetFrameworks>" + Environment.NewLine +
                "    <EnableWindowsTargeting>true</EnableWindowsTargeting>",
                StringComparison.Ordinal));
    }

    private static void Restore(string project)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("dotnet", ["restore", project, "-nologo"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Restoring '{project}' failed.{Environment.NewLine}{output}");
        }
    }

    /// <summary>
    /// Gives every test of the sample suite a sentence for a display name, the way a team that
    /// writes its tests as prose does. The method names are untouched.
    /// </summary>
    public void GiveTheTestsDisplayNames()
    {
        string file = Path.Combine(Root, "Sample.Library.Tests", "AgesTests.cs");
        int count = 0;

        string text = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(file),
            @"\[(Fact|Theory)\]",
            match => $"[{match.Groups[1].Value}(DisplayName = \"Sentence number {++count}.\")]");

        File.WriteAllText(file, text);
    }

    private static FixtureCopy CopyOf(string source)
    {
        string destination = Path.Combine(TempRoot, $"killmutants-e2e-{Guid.NewGuid():N}");

        CopyDirectory(source, destination);

        return new FixtureCopy(destination);
    }

    /// <summary>
    /// The temporary directory, with every symbolic link on the way to it resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, after macOS failed three tests that pass everywhere else with
    /// <c>CS0103: The name 'Money' does not exist in the current context</c>. On macOS
    /// <c>Path.GetTempPath()</c> returns a path under <c>/var</c>, which is a symbolic link to
    /// <c>/private/var</c>. MSBuild resolves the link for the referenced projects and not for the
    /// one it was given - the restore log says <c>/private/var/.../Sample.Support</c> on one line
    /// and <c>/var/.../Sample.Library.Tests</c> on the next - and the transitive project reference
    /// is lost between the two spellings: <c>Tests -&gt; Support -&gt; Library</c> compiles without
    /// <c>Library</c>.
    /// </para>
    /// <para>
    /// It is not a macOS defect and it is not ours. Reproduced on Linux by planting the same fixture
    /// behind a symbolic link and building through it: the identical error, from plain
    /// <c>dotnet build</c>, with KillMutants not involved. What is ours is handing MSBuild a path
    /// shape no real repository has, so the link is resolved once, here, and every fixture is built
    /// at the path a user's would be.
    /// </para>
    /// </remarks>
    private static string TempRoot { get; } = Canonical(Path.GetTempPath());

    /// <summary>Resolves every symbolic link on a path, from the root down.</summary>
    /// <remarks>
    /// There is no <c>realpath</c> in .NET. <see cref="FileSystemInfo.ResolveLinkTarget"/> answers
    /// for one component, so the path is walked from the top: a link anywhere along it - and on
    /// macOS the link is <c>/var</c>, four levels above the leaf - is resolved before the next
    /// component is appended.
    /// </remarks>
    private static string Canonical(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));

        if (directory.Parent is null)
        {
            return directory.FullName;
        }

        string resolved = Path.Combine(Canonical(directory.Parent.FullName), directory.Name);

        return new DirectoryInfo(resolved).ResolveLinkTarget(returnFinalTarget: true)?.FullName
               ?? resolved;
    }

    /// <remarks>
    /// Windows reports a locked file as <see cref="UnauthorizedAccessException"/> rather than
    /// <see cref="IOException"/>, so catching only the latter meant this teardown failed tests whose
    /// every assertion had passed. And the lock is ours: KillMutants loads a project's generators
    /// into <c>AssemblyLoadContext.Default</c> and never unloads them, which on Windows holds the
    /// file open for the life of the process. That is RB-020's second consequence, recorded there.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>The repository's own single-project fixture, resolved from this file's path.</summary>
    public static string SourceFixtureDirectory { get; } = FixtureDirectory("single");

    private static string FixtureDirectory(string name, [CallerFilePath] string sourceFilePath = "")
    {
        // <root>/tests/KillMutants.EndToEnd.Tests/FixtureCopy.cs
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

        return Path.Combine(repositoryRoot, "tests", "fixtures", name);
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
