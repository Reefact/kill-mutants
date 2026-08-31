using KillMutants.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// Regression tests for the warnings-as-errors trap. A mutation frequently makes live code
/// unreachable (CS0162) or a variable unused (CS0219). If those still fail the compilation, the
/// mutant is recorded as a compile error instead of being tested, and the score is understated for
/// a reason that has nothing to do with the tests.
/// </summary>
[Collection(nameof(SerialFixtureAccess))]
public class WarningsAsErrorsTests
{
    [Fact]
    public void A_diagnostic_escalated_to_an_error_no_longer_fails_the_compilation()
    {
        // WithGeneralDiagnosticOption alone does NOT clear these: /warnaserror+:CS0162 lands in
        // SpecificDiagnosticOptions, which that call leaves untouched. Verified against Roslyn 5.9.
        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            ["/target:library", "/out:x.dll", "/warnaserror+:CS0162,CS0219", "x.cs"],
            baseDirectory: Path.GetTempPath(),
            sdkDirectory: null);

        Assert.Equal(
            Microsoft.CodeAnalysis.ReportDiagnostic.Error,
            parsed.CompilationOptions.SpecificDiagnosticOptions["CS0162"]);

        CSharpCompilationOptions relaxed = ProjectCompilation.RelaxWarningsAsErrors(parsed.CompilationOptions);

        Assert.Equal(
            Microsoft.CodeAnalysis.ReportDiagnostic.Warn,
            relaxed.SpecificDiagnosticOptions["CS0162"]);
        Assert.Equal(
            Microsoft.CodeAnalysis.ReportDiagnostic.Warn,
            relaxed.SpecificDiagnosticOptions["CS0219"]);
    }

    [Fact]
    public void A_deliberate_suppression_is_left_alone()
    {
        // The user silenced these on purpose, and honouring that cannot cost us a mutant.
        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            ["/target:library", "/out:x.dll", "/nowarn:CS0168", "x.cs"],
            baseDirectory: Path.GetTempPath(),
            sdkDirectory: null);

        CSharpCompilationOptions relaxed = ProjectCompilation.RelaxWarningsAsErrors(parsed.CompilationOptions);

        Assert.Equal(
            Microsoft.CodeAnalysis.ReportDiagnostic.Suppress,
            relaxed.SpecificDiagnosticOptions["CS0168"]);
    }
}
