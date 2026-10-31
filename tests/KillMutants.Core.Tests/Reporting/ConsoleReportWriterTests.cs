using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// Rendering is tested against constructed reports rather than a real run, so that growing the
/// mutator catalogue cannot silently change what these assertions mean.
/// </summary>
public class ConsoleReportWriterTests
{
    private static IReadOnlyList<Mutant> MutantsFor(string source) =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From(source, "/src/Ages.cs"));

    /// <summary>
    /// A partial run, because only one compares against an earlier state and so only one can find
    /// that state incomplete. The first version of these tests left the scope at its default, and
    /// rendered nothing at all: everything about a comparison is written under that gate, which is
    /// where it belongs.
    /// </summary>
    private static readonly RunScope Partial = new("0123456789abcdef", "fedcba9876543210", true, 3);

    private static string Render(params MutantResult[] results)
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(writer, new MutationTestReport(results));

        return writer.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void A_run_where_every_mutant_died_renders_the_headline_figures()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];

        Assert.Equal(
            """
            KillMutants

            Mutants: 1
            Killed: 1
            Survived: 0

            Mutation score: 100%

            """.ReplaceLineEndings("\n"),
            Render(new MutantResult(mutant, MutantStatus.Killed)));
    }

    [Fact]
    public void Survivors_are_named_before_the_totals_because_they_are_the_point_of_the_run()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = Render(
            new MutantResult(mutants[0], MutantStatus.Survived),
            new MutantResult(mutants[1], MutantStatus.Killed));

        Assert.Equal(
            """
            KillMutants

            Survived (1)

              Ages.cs
                1:28  a >= 18 -> a > 18  [Comparison]

            Mutants: 2
            Killed: 1
            Survived: 1

            Mutation score: 50%

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void Findings_are_grouped_by_file_and_lined_up()
    {
        // Two files, several mutants each, deliberately out of order.
        IReadOnlyList<Mutant> mutants = MutantsFor(
            "class C { bool M(int a) => a >= 18; bool N(int a, bool b) => b && a >= 5; }");

        string output = Render([.. mutants.Select(m => new MutantResult(m, MutantStatus.Survived))]);

        Assert.Contains("Survived (6)", output, StringComparison.Ordinal);
        Assert.Contains("  Ages.cs", output, StringComparison.Ordinal);
        Assert.Contains("[LogicalOperator]", output, StringComparison.Ordinal);

        // The point of the padding: every arrow lands in the same column, so a block of findings
        // reads as a table rather than as ragged text.
        int[] arrowColumns = [.. output
            .Split('\n')
            .Where(line => line.Contains(" -> ", StringComparison.Ordinal))
            .Select(line => line.IndexOf(" -> ", StringComparison.Ordinal))];

        Assert.Equal(6, arrowColumns.Length);
        Assert.Single(arrowColumns.Distinct());
    }

    /// <summary>
    /// Regression test (#52). One long literal used to widen every finding of its file to its own
    /// length; the console now shows each text on one line, at most <see cref="ConsoleReportWriter.ShownLength"/>
    /// characters, and lines the block up on what it shows.
    /// </summary>
    [Fact]
    public void A_long_literal_is_shortened_and_no_longer_widens_its_neighbours()
    {
        string description = new('x', 1253);
        IReadOnlyList<Mutant> mutants = MutantsFor(
            $"class C {{ string D() => \"{description}\"; bool M(int a) => a >= 18; }}");

        string output = Render([.. mutants.Select(m => new MutantResult(m, MutantStatus.Survived))]);
        string[] findings = [.. output.Split('\n').Where(line => line.Contains(" -> ", StringComparison.Ordinal))];

        Assert.All(findings, line => Assert.True(line.Length < 160, line));
        Assert.Contains(findings, line => line.Contains('…', StringComparison.Ordinal));
        Assert.Single(findings.Select(line => line.IndexOf(" -> ", StringComparison.Ordinal)).Distinct());
    }

    /// <summary>A text spread over several lines is shown on one, so each finding stays one line of the table.</summary>
    [Theory]
    [InlineData("a\n    && b", "a && b")]
    [InlineData("  f(\r\n\tx);  ", "f( x);")]
    public void A_text_over_several_lines_is_shown_on_one(string text, string expected)
    {
        Assert.Equal(expected, ConsoleReportWriter.Shown(text));
    }

    [Fact]
    public void Uncovered_mutants_are_named_too_because_untested_code_is_its_own_finding()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];

        string output = Render(new MutantResult(mutant, MutantStatus.NoCoverage));

        Assert.Contains("No coverage (1)", output, StringComparison.Ordinal);
        Assert.Contains("Ages.cs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_elapsed_time_is_shown_when_it_was_measured()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport([new MutantResult(mutant, MutantStatus.Killed)], TimeSpan.FromSeconds(12.34)));

        Assert.Contains("Elapsed: 12.3 s", writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An incomplete comparison replaces the verdict rather than being printed beside it.
    /// </summary>
    /// <remarks>
    /// Review found this in the abstention itself. The warning was added ahead of the verdict and
    /// the verdict was left alone, so a run whose mutants all died printed "Comparison incomplete"
    /// and then "Verdict: no undetected mutant in the selected scope" - which is exactly the green
    /// the abstention exists to withhold, in the one place a person actually reads.
    /// </remarks>
    [Fact]
    public void An_incomplete_comparison_prints_no_verdict()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(mutant, MutantStatus.Killed)],
                scope: Partial,
                unreadComponents: ["libs/Sample"]));

        string output = writer.ToString();

        Assert.Contains("Comparison incomplete", output, StringComparison.Ordinal);
        Assert.Contains("No verdict", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Verdict:", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caveat is printed before the findings it makes provisional, not after them.
    /// </summary>
    /// <remarks>
    /// Review found this, and what it found is the shape of the gap: the two tests above assert that
    /// the text is present, which says nothing about where. It sat in the scope section, which this
    /// writer emits last, so it arrived after the findings a reader had already read - and told them
    /// "the mutants below" about mutants printed above it. Asserting an order is the only way to pin
    /// a defect that is entirely about order.
    /// </remarks>
    [Fact]
    public void The_incomplete_comparison_caveat_precedes_the_findings()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(mutant, MutantStatus.Survived)],
                scope: Partial,
                unreadComponents: ["libs/Sample"]));

        string output = writer.ToString();
        int caveat = output.IndexOf("Comparison incomplete", StringComparison.Ordinal);
        int findings = output.IndexOf("Survived", StringComparison.Ordinal);

        Assert.True(caveat >= 0 && findings >= 0, $"both expected in:{Environment.NewLine}{output}");
        Assert.True(caveat < findings, $"caveat at {caveat}, findings at {findings}");
    }

    /// <summary>
    /// The subtlest of the three: a change with no mutants also reads as a settled pass.
    /// </summary>
    [Fact]
    public void An_incomplete_comparison_over_a_change_with_no_mutants_prints_no_verdict()
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport([], scope: Partial, unreadComponents: ["libs/Sample"]));

        string output = writer.ToString();

        Assert.Contains("No verdict", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Nothing in the change produces a mutant.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeouts_and_compile_errors_are_only_shown_when_they_happened()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = Render(
            new MutantResult(mutants[0], MutantStatus.Timeout),
            new MutantResult(mutants[1], MutantStatus.CompileError, "CS0162"));

        Assert.Contains("Timed out: 1", output, StringComparison.Ordinal);
        Assert.Contains("Compile errors: 1", output, StringComparison.Ordinal);

        // A mutant that could not be tested is excluded from the score rather than counted against
        // the suite; a timeout is a detection.
        Assert.Contains("Mutation score: 100%", output, StringComparison.Ordinal);
    }

    private static string RenderStopped(RunScope scope, int? selected, params MutantResult[] results)
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer, new MutationTestReport(results, scope: scope, stopped: new EarlyStop(selected)));

        return writer.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// First, before the findings it qualifies, for the reason the comparison caveat is: a reader
    /// has to know the run did not get to everything before reading what it found.
    /// </summary>
    [Fact]
    public void A_stopped_run_says_so_before_anything_else_and_prints_no_score()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = RenderStopped(
            RunScope.WholeCodebase,
            selected: 9,
            new MutantResult(mutants[0], MutantStatus.Killed),
            new MutantResult(mutants[1], MutantStatus.Survived));

        Assert.StartsWith(
            "KillMutants\n\nStopped before the end: 2 of the 9 mutant(s) it selected have a verdict.",
            output,
            StringComparison.Ordinal);
        Assert.True(
            output.IndexOf("Stopped before the end", StringComparison.Ordinal) <
            output.IndexOf("Survived (1)", StringComparison.Ordinal));
        Assert.DoesNotContain("Mutation score:", output, StringComparison.Ordinal);
        Assert.Contains("No mutation score: the run stopped", output, StringComparison.Ordinal);

        // Nor the per-family ratio, which is the same number in miniature.
        Assert.DoesNotContain("%", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A partial run's verdict is a claim about its whole selection, and a stopped one has not judged
    /// it - whatever it found among the mutants it did reach, and whatever the comparison says.
    /// </summary>
    [Fact]
    public void A_stopped_partial_run_gives_no_verdict()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = RenderStopped(
            Partial, selected: 9, new MutantResult(mutants[0], MutantStatus.Killed));

        Assert.Contains(
            "No verdict: 0 of 1 mutant(s) judged were undetected, and the run stopped before it " +
            "had judged the rest.",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Verdict:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_stopped_before_it_selected_anything_says_so()
    {
        string output = RenderStopped(RunScope.Unresolved, selected: null);

        Assert.Contains("it had not yet selected a single mutant", output, StringComparison.Ordinal);
        Assert.Contains(
            "No verdict: the run stopped before it had judged a single mutant.",
            output,
            StringComparison.Ordinal);

        // A change it never read has no file count, and a zero would say nothing changed.
        Assert.Contains("Scope: a change the run stopped before resolving\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("files changed", output, StringComparison.Ordinal);
    }

    private static PlannedRun Run(int verdicts) =>
        new("2026-10-11T20:00:00.0000000+00:00", "test", ".NET 10", "Linux", 4, 2, 0, verdicts, 0, []);

    /// <summary>
    /// A run that inherited everything writes a complete report in minutes; a reader has to know its
    /// counts are the sum of several runs before reading them as one (DEC0015).
    /// </summary>
    [Fact]
    public void A_resumed_series_says_what_it_inherited_before_the_counts()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(mutant, MutantStatus.Killed)],
                series: new RunSeries("nightly", 540, [Run(540), Run(1)])));

        string output = writer.ToString();

        Assert.Contains("Series 'nightly': run 2, 540 verdict(s) inherited from 1 earlier run(s), 1 reached", output, StringComparison.Ordinal);
        Assert.True(
            output.IndexOf("Series 'nightly'", StringComparison.Ordinal) <
            output.IndexOf("Mutants:", StringComparison.Ordinal),
            "the series caveat must come before the counts it qualifies");
    }

    [Fact]
    public void The_first_run_of_a_series_says_it_inherited_nothing()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(mutant, MutantStatus.Killed)],
                series: new RunSeries("nightly", 0, [Run(1)])));

        Assert.Contains("Series 'nightly': first run, nothing inherited.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_names_no_series_says_nothing_about_one()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(writer, new MutationTestReport([new MutantResult(mutant, MutantStatus.Killed)]));

        Assert.DoesNotContain("Series", writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression test (#22). A partial run that DEC0011 widened says so beside its scope line, and
    /// says which change made it: that line describes the diff alone, and a run over most of a
    /// codebase announced itself as a run over a few files.
    /// </summary>
    [Fact]
    public void A_widened_run_names_what_it_took_whole_and_the_change_that_made_it()
    {
        Mutant changed = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        Mutant untouched = MutantsFor("class D { bool M(int a) => a >= 18; }", "/src/Other.cs")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [
                    new MutantResult(changed, MutantStatus.Survived),
                    new MutantResult(untouched, MutantStatus.Survived),
                ],
                scope: Partial,
                widened:
                [
                    new WidenedProject("src/Core/Core.csproj", ["tests/Core.Tests/AgesTests.cs"]),
                ],
                inTheChange: path => path == "/src/Ages.cs"));

        string output = OneLine(writer.ToString());

        Assert.Contains(
            "Widened: Core was mutated whole rather than file by file, because the change " +
            "touches tests/Core.Tests/AgesTests.cs (DEC0011). 1 of the 2 undetected mutants " +
            "are in files the change did not touch.",
            output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same sentence with one undetected mutant agrees with it, rather than printing "mutant(s)
    /// are" whatever the count - which review found reading wrong at exactly the count of one.
    /// </summary>
    [Fact]
    public void A_widened_run_with_one_undetected_mutant_says_so_in_the_singular()
    {
        Mutant untouched = MutantsFor("class D { bool M(int a) => a >= 18; }", "/src/Other.cs")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(untouched, MutantStatus.Survived)],
                scope: Partial,
                widened:
                [
                    new WidenedProject("src/Core/Core.csproj", ["tests/Core.Tests/AgesTests.cs"]),
                ],
                inTheChange: path => path == "/src/Ages.cs"));

        Assert.Contains(
            "(DEC0011). 1 of the 1 undetected mutant is in a file the change did not touch.",
            OneLine(writer.ToString()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The counterweight. A run that stayed precise has nothing to add to its scope, and a line
    /// saying "nothing was widened" on every pull request would be read by nobody.
    /// </summary>
    [Fact]
    public void A_partial_run_that_stayed_precise_says_nothing_about_widening()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [new MutantResult(mutant, MutantStatus.Survived)], scope: Partial));

        Assert.DoesNotContain("Widened", writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A change to a shared build file can widen every project there is, and a sentence naming all
    /// of them is a sentence nobody finishes. The report file keeps them all.
    /// </summary>
    [Fact]
    public void A_long_widening_names_the_first_few_and_counts_the_rest()
    {
        var writer = new StringWriter();
        string[] because = ["a/One.cs", "b/Two.cs", "c/Three.cs", "d/Four.cs", "e/Five.cs"];

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [],
                scope: Partial,
                widened: [new WidenedProject("src/Core/Core.csproj", because)]));

        Assert.Contains(
            "because the change touches a/One.cs, b/Two.cs, c/Three.cs and 2 more (DEC0011).",
            OneLine(writer.ToString()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Two projects can share a name in two directories, and naming both by it names neither: review
    /// found "Core and Core were mutated whole".
    /// </summary>
    [Fact]
    public void Two_widened_projects_that_share_a_name_are_told_apart_by_their_paths()
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport(
                [],
                scope: Partial,
                widened:
                [
                    new WidenedProject("src/A/Core.csproj", ["tests/A.Tests/ATests.cs"]),
                    new WidenedProject("src/B/Core.csproj", ["tests/A.Tests/ATests.cs"]),
                    new WidenedProject("src/Domain/Domain.csproj", ["tests/A.Tests/ATests.cs"]),
                ]));

        Assert.Contains(
            "Widened: src/A/Core.csproj, src/B/Core.csproj and Domain were mutated whole",
            OneLine(writer.ToString()),
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<Mutant> MutantsFor(string source, string path) =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(source, path));

    /// <summary>The console wraps at a terminal's width, so a sentence is asserted whole.</summary>
    private static string OneLine(string output) =>
        string.Join(
            ' ',
            output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
}
