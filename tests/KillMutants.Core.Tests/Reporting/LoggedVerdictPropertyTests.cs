using CsCheck;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using KillMutants.Testing;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A later run of the series inherits what <see cref="LoggedVerdict.Parse"/> reads back from what
/// <see cref="JsonReportWriter.Line"/> wrote (DEC0015), so the two must agree on every verdict and
/// not only on the tidy ones: a detail is a compiler's message, with its line breaks and its quotes,
/// and a test name is whatever a display name made of it.
/// </summary>
public class LoggedVerdictPropertyTests
{
    private static readonly Lazy<IReadOnlyList<Mutant>> Mutants = new(() =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(
            "class C { int M(int a, int b) => a > b ? a + b : a * b - 1; bool N(bool x) => !x && true; }",
            "/src/Sample.cs")));

    /// <summary>Text of any kind, from the plainest to the kind that has to be escaped.</summary>
    /// <remarks>
    /// Assembled from whole pieces, so a character outside the basic plane is drawn as the pair it
    /// is and never cut in half. A lone surrogate is the one text this does not draw, because no
    /// line of a UTF-8 file can hold one: the writer puts U+FFFD in its place, measured, and so
    /// would any format written in UTF-8. Nothing this tool reads - a compiler's message, a test's
    /// display name - is expected to carry one, and if it does, the verdict is still inherited; only
    /// that character of its text is not.
    /// </remarks>
    private static readonly Gen<string> Text = Gen.OneOf(
        Gen.String,
        Gen.OneOf(
                "a", "Z", " ", "\"", "\\", "/", "\n", "\r\n", "\t", "{", "}", "[", "]", ":", ",", "<", ">=",
                "&", "'", "\u0000", "\u001f", "\u2028", "\u00e9", "\u6f22", "\U0001F600", "CS0029")
            .Array[0, 8]
            .Select(string.Concat));

    private static readonly Gen<string?> OptionalText =
        Gen.Bool.SelectMany(absent => absent ? Gen.Const((string?)null) : Text.Select(text => (string?)text));

    /// <summary>Any verdict the log can be told, on one of a handful of real mutants.</summary>
    internal static readonly Gen<MutantResult> Results =
        from index in Gen.Int[0, 1_000]
        from status in Gen.Enum<MutantStatus>()
        from detail in OptionalText
        from killedBy in Text.Where(name => !string.IsNullOrWhiteSpace(name)).Array[0, 3]
        from overturned in OptionalText
        select new MutantResult(
            Mutants.Value[index % Mutants.Value.Count],
            status,
            detail,
            [.. killedBy.Select(TestName.Create)],
            Overturned: overturned);

    /// <summary>
    /// Fails unless <paramref name="read"/> carries everything a later run inherits from
    /// <paramref name="written"/>, compared character for character - not through another round of
    /// serialisation, which would mangle a character the same way on both sides and agree.
    /// </summary>
    internal static void AssertInherits(MutantResult written, LoggedVerdict read)
    {
        Assert.Equal(written.Mutant.Key.ToString(), read.Key);
        Assert.Equal(written.Status, read.Status);
        Assert.Equal(written.Detail, read.Detail);
        Assert.Equal(
            written.KilledBy.Select(test => test.ToString()),
            read.KilledBy.Select(test => test.ToString()));
        Assert.Equal(written.Overturned, read.Overturned);
    }

    /// <summary>A failing case shown as the line it was written as, rather than as a syntax tree.</summary>
    internal static string Print(MutantResult result) => JsonReportWriter.Line(result);

    [Fact]
    public void Every_verdict_reads_back_as_it_was_written()
    {
        Assert.NotEmpty(Mutants.Value);

        Results.Sample(
            result =>
            {
                string line = JsonReportWriter.Line(result);

                // One line is what a line of JSON Lines has to be, whatever the text inside it.
                Assert.DoesNotContain('\n', line);
                Assert.DoesNotContain('\r', line);

                AssertInherits(result, LoggedVerdict.Parse(line));
            },
            iter: 5_000,
            print: Print);
    }
}
