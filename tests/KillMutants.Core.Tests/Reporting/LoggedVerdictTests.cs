using System.Text.Json;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using KillMutants.Testing;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A line of the log is written by <see cref="JsonReportWriter.Line"/> and read back by a later run
/// of the series (DEC0015); the two have to agree on what a verdict is.
/// </summary>
public class LoggedVerdictTests
{
    private static Mutant FirstMutant() =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From("class C { bool M(int a) => a >= 18; }", "/src/Ages.cs"))[0];

    [Fact]
    public void A_kill_reads_back_with_its_tests()
    {
        Mutant mutant = FirstMutant();
        var written = new MutantResult(
            mutant, MutantStatus.Killed, KilledBy: [TestName.Create("Ns.AgesTests.Refuses_a_minor")]);

        LoggedVerdict read = LoggedVerdict.Parse(JsonReportWriter.Line(written));

        Assert.Equal(mutant.Key.ToString(), read.Key);
        Assert.Equal(MutantStatus.Killed, read.Status);
        Assert.Equal(["Ns.AgesTests.Refuses_a_minor"], read.KilledBy.Select(test => test.ToString()));
        Assert.Null(read.Detail);
        Assert.Null(read.Overturned);
    }

    [Fact]
    public void A_corrected_verdict_keeps_how_it_was_corrected()
    {
        var written = new MutantResult(FirstMutant(), MutantStatus.Survived, Overturned: "timed out, then Survived alone");

        LoggedVerdict read = LoggedVerdict.Parse(JsonReportWriter.Line(written));

        Assert.Equal(MutantStatus.Survived, read.Status);
        Assert.Equal("timed out, then Survived alone", read.Overturned);
        Assert.Empty(read.KilledBy);
    }

    [Fact]
    public void A_compile_error_keeps_its_detail()
    {
        var written = new MutantResult(FirstMutant(), MutantStatus.CompileError, "CS0029: cannot convert");

        Assert.Equal("CS0029: cannot convert", LoggedVerdict.Parse(JsonReportWriter.Line(written)).Detail);
    }

    [Theory]
    [InlineData("""{"status":"Killed"}""")]
    [InlineData("""{"key":"abc","status":"Eaten"}""")]
    [InlineData("""{"key":"","status":"Killed"}""")]
    [InlineData("""["key"]""")]
    public void A_line_that_is_not_a_verdict_is_refused(string line)
    {
        Assert.Throws<FormatException>(() => LoggedVerdict.Parse(line));
    }

    [Fact]
    public void A_line_that_is_not_json_is_refused()
    {
        Assert.ThrowsAny<JsonException>(() => LoggedVerdict.Parse("""{"key":"abc","sta"""));
    }
}
