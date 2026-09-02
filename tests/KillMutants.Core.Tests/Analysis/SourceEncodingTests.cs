using System.Text;
using KillMutants.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// A file with no byte order mark carries no clue about its own encoding, so the compiler is told
/// which one to use and we have to be told the same thing. Decoding it differently is silent: the
/// file still parses, and its string constants are simply not the ones the build produces.
/// </summary>
public class SourceEncodingTests
{
    // 'é' is one byte (0xE9) in Windows-1252 and two in UTF-8, and 0xE9 alone is not valid UTF-8.
    private const string Source = "class C { public const string Name = \"café\"; }";

    [Fact]
    public void A_source_file_is_decoded_with_the_encoding_the_compiler_was_given()
    {
        using var file = TemporarySource.EncodedAs(Source, CodePagesEncodingProvider.Instance
            .GetEncoding(1252)!);

        ProjectCompilation compilation = Compile(file.Path, codepage: 1252);

        Assert.Contains("café", TextOf(compilation), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the same bytes read as UTF-8 do not produce that text, or the test above would pass
    /// whatever we did.
    /// </summary>
    [Fact]
    public void The_same_bytes_read_as_utf8_are_not_the_same_program()
    {
        using var file = TemporarySource.EncodedAs(Source, CodePagesEncodingProvider.Instance
            .GetEncoding(1252)!);

        ProjectCompilation compilation = Compile(file.Path, codepage: null);

        Assert.DoesNotContain("café", TextOf(compilation), StringComparison.Ordinal);
    }

    private static string TextOf(ProjectCompilation compilation) =>
        string.Join("\n", compilation.Compilation.SyntaxTrees.Select(tree => tree.ToString()));

    private static ProjectCompilation Compile(string path, int? codepage)
    {
        string[] arguments =
        [
            "/noconfig",
            "/target:library",
            "/out:Sample.dll",
            .. codepage is { } page ? new[] { $"/codepage:{page}" } : [],
            path,
        ];

        return ProjectCompilation.Create(
            CscCommandLine.Parse(arguments, Path.GetDirectoryName(path)!),
            Path.GetDirectoryName(path)!);
    }

    private sealed class TemporarySource : IDisposable
    {
        private TemporarySource(string path) => Path = path;

        public string Path { get; }

        public static TemporarySource EncodedAs(string content, Encoding encoding)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"killmutants-test-{Guid.NewGuid():N}.cs");

            // Written without a preamble on purpose: a byte order mark would tell Roslyn the answer,
            // and the whole question is what happens when nothing does.
            File.WriteAllBytes(path, encoding.GetBytes(content));

            return new TemporarySource(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
