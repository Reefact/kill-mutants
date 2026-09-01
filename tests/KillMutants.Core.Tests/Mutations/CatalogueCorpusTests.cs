using KillMutants.Coverage;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// The catalogue's assumptions about Roslyn, checked by compiling what it proposes instead of
/// reasoning about it. Two properties are asserted over a corpus of awkward C#: every proposed
/// mutant compiles, and every proposed mutant emits something different from the baseline.
/// </summary>
/// <remarks>
/// <para>
/// These are the two ways a mutator can be quietly wrong. A mutant that does not compile is only
/// wasted work, but a mutant that compiles to the <em>same</em> IL can never be killed, so it is
/// reported as a gap in the user's tests that does not exist - see RB-001. Per-family unit tests
/// check the rule each family implements; this checks the families against the language.
/// </para>
/// <para>
/// The corpus is chosen for the assumptions most likely to be wrong rather than for coverage:
/// user-defined operators, lifted operators over nullable types, target typing, implicit
/// conversions, types with only one operator of a pair, and the newer language forms.
/// </para>
/// </remarks>
public class CatalogueCorpusTests
{
    public static TheoryData<string, string, string> Corpus =>
        new()
        {
            { "lifted comparison and arithmetic", "int? a, int? b", "object? M({0}) => a > b ? a + b : a - b;" },
            { "lifted equality", "int? a, int? b", "bool M({0}) => a == b;" },
            { "lifted logical over bool?", "bool? a, bool? b", "bool? M({0}) => a & b;" },
            { "coalesce widening to object", "string a, object b", "object M({0}) => a ?? b;" },
            { "coalesce with side effects", "string? a", "string M({0}) => a ?? System.Guid.NewGuid().ToString();" },
            { "target-typed conditional", "bool f, int x", "int? M({0}) => f ? x : null;" },
            { "conditional over a common base", "bool f, string s", "object M({0}) => f ? s : (object)1;" },
            { "date plus span, which does have one", "System.DateTime a, System.TimeSpan b", "object M({0}) => a + b;" },
            { "decimal arithmetic", "decimal a, decimal b", "decimal M({0}) => a * b + a % b;" },
            { "char arithmetic promotes to int", "char a", "int M({0}) => a + 1;" },
            { "native integers", "nint a, nint b", "nint M({0}) => a + b;" },
            { "checked arithmetic", "int a, int b", "int M({0}) { checked { return a + b; } }" },
            { "enum flags", "System.IO.FileAccess a, System.IO.FileAccess b", "bool M({0}) => (a & b) != 0;" },
            { "interpolation and a raw string", "string a", "string M({0}) => $\"x{a}y\" + \"\"\"z\"\"\";" },
            { "an operator inside an interpolation hole", "int a, int b", "string M({0}) => $\"{a + b} of {a} did not pass\";" },
            { "a conditional inside an interpolation hole", "bool f, int a", "string M({0}) => $\"{(f ? a : 0)} left\";" },
            { "compound assignment on a string", "string a", "string M({0}) { a += \"x\"; return a; }" },
            { "compound assignment and increment", "int a", "int M({0}) { a += 1; a *= 2; a++; --a; return a; }" },
            { "switch expression with patterns", "object o", "int M({0}) => o switch { int i when i >= 3 => i, string s => s.Length, _ => 0 };" },
            { "local function and static lambda", "int a", "int M({0}) { static int F(int v) => v * 2; System.Func<int, int> g = static v => v + 1; return F(a) + g(a); }" },
            { "spans", "System.Span<int> a, System.Span<int> b, bool f", "int M({0}) => (f ? a : b).Length >= 1 ? 1 : 0;" },
            { "unary minus and bitwise complement", "int a", "int M({0}) => -a + ~a;" },
            { "null-conditional and null-coalescing assignment", "string? a", "int M({0}) { a ??= \"x\"; return a?.Length ?? 0; }" },
        };

    /// <summary>
    /// The other half of the corpus: awkward C# the catalogue is <em>supposed</em> to decline, each
    /// for a reason a family test pins down in detail. They are listed here so that a change which
    /// silently starts proposing them fails something.
    /// </summary>
    public static TheoryData<string, string, string> Declined =>
        new()
        {
            // No `string - string` exists, so an arithmetic mutant could only fail to compile (RB-011).
            { "string concatenation", "string a, string b", "string M({0}) => a + b;" },

            // `DateTime - DateTime` is defined; `DateTime + DateTime` is not. Same rule, no list of
            // special cases.
            { "date difference", "System.DateTime a, System.DateTime b", "object M({0}) => a - b;" },

            // Dropping the fallback would leave an `int?` where an `int` is required (RB-015).
            { "a coalesce that removes nullability", "int? a", "int M({0}) => a ?? 0;" },

            // The ternary declares `first`, and swapping its branches would orphan it (RB-016).
            { "a conditional over a list pattern", "int[] xs", "int M({0}) => xs is [var first, ..] ? first : xs[^1];" },
        };

    /// <summary>
    /// A mutant that cannot compile is cost without signal - RB-011 - and every family is supposed to
    /// ask the compiler before proposing one. This is what fails when one of them stops asking.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_proposed_mutant_compiles(string description, string parameters, string member)
    {
        (Compilation compilation, IReadOnlyList<Mutant> mutants) = Analyse(parameters, member);

        Assert.All(mutants, mutant =>
        {
            // Errors only, which is what the real pipeline sees: mutant compilations have
            // warnings-as-errors relaxed (RB-004), and some mutations legitimately trip a warning -
            // dropping a null-coalescing fallback returns a maybe-null value (RB-015).
            Diagnostic[] errors = [.. Emit(compilation, mutant).Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

            Assert.True(
                errors.Length == 0,
                $"{description}: '{mutant.OriginalText}' -> '{mutant.MutatedText}' [{mutant.Mutator}] " +
                $"does not compile: {string.Join("; ", errors.Select(d => d.GetMessage()))}");
        });
    }

    /// <summary>
    /// The worse failure. Roslyn binds and emits from the node kind, and a replacement that prints
    /// as a change while emitting the original IL is silently equivalent: it can never be killed, so
    /// it is reported as a gap in the tests that does not exist.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_proposed_mutant_emits_something_different(string description, string parameters, string member)
    {
        (Compilation compilation, IReadOnlyList<Mutant> mutants) = Analyse(parameters, member);
        byte[] baseline = EmitBytes(compilation);

        Assert.All(mutants, mutant =>
        {
            byte[] mutated = EmitBytes(compilation, mutant);

            Assert.False(
                baseline.AsSpan().SequenceEqual(mutated),
                $"{description}: '{mutant.OriginalText}' -> '{mutant.MutatedText}' [{mutant.Mutator}] " +
                "emits an assembly identical to the baseline, so no test could ever kill it.");
        });
    }

    [Theory]
    [MemberData(nameof(Declined))]
    public void The_catalogue_declines_what_it_cannot_usefully_mutate(
        string description, string parameters, string member)
    {
        Assert.True(
            Generate(parameters, member).Count == 0,
            $"{description}: the catalogue proposed a mutation it is supposed to decline.");
    }

    /// <summary>
    /// The instrumentation's own corpus. The recorder returns its argument, so it cannot change what
    /// an expression evaluates to - but it can change how the surrounding source <em>parses</em>, and
    /// when it does the whole run stops before a single mutant is tested.
    /// </summary>
    /// <remarks>
    /// Found the hard way: a mutation site inside an interpolation hole became
    /// <c>$"{global::…Hit(1, a + b)}"</c>, where the colon reads as the start of a format specifier.
    /// The build failed with CS0103 and no coverage could be measured at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Instrumenting_every_site_leaves_the_project_compiling(
        string description, string parameters, string member)
    {
        (Compilation compilation, IReadOnlyList<Mutant> mutants) = Analyse(parameters, member);
        MutationSites sites = MutationSites.From(mutants, compilation);

        if (sites.IdentifierByNode.Count == 0)
        {
            return;
        }

        SyntaxTree tree = compilation.SyntaxTrees.Single();
        SyntaxNode instrumented = CoverageRewriter.Instrument(
            tree.GetRoot(TestContext.Current.CancellationToken), sites.IdentifierByNode);

        Compilation probed = compilation
            .ReplaceSyntaxTree(tree, tree.WithRootAndOptions(instrumented, tree.Options))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                CoverageProbe.Source, cancellationToken: TestContext.Current.CancellationToken));

        Diagnostic[] errors = [.. probed
            .Emit(new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken)
            .Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        Assert.True(
            errors.Length == 0,
            $"{description}: instrumenting the sites broke the build: " +
            string.Join("; ", errors.Select(diagnostic => diagnostic.ToString())));
    }

    private static (Compilation Compilation, IReadOnlyList<Mutant> Mutants) Analyse(
        string parameters, string member)
    {
        Compilation compilation = TestCompilation.From(
            $"#nullable enable{Environment.NewLine}public class C {{ public {member.Replace("{0}", parameters, StringComparison.Ordinal)} }}");

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default).Generate(compilation);

        // Without this the assertions below pass vacuously on any entry the catalogue ignores, and
        // the corpus would quietly stop testing the thing it was written for. The cases the
        // catalogue is meant to decline live in Declined instead.
        Assert.NotEmpty(mutants);

        return (compilation, mutants);
    }

    private static IReadOnlyList<Mutant> Generate(string parameters, string member)
    {
        Compilation compilation = TestCompilation.From(
            $"#nullable enable{Environment.NewLine}public class C {{ public " +
            $"{member.Replace("{0}", parameters, StringComparison.Ordinal)} }}");

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        return new MutantGenerator(MutatorCatalog.Default).Generate(compilation);
    }

    private static EmitResult Emit(Compilation compilation, Mutant mutant) =>
        Mutate(compilation, mutant).Emit(new MemoryStream());

    private static byte[] EmitBytes(Compilation compilation, Mutant? mutant = null)
    {
        var stream = new MemoryStream();
        EmitResult result = (mutant is null ? compilation : Mutate(compilation, mutant)).Emit(stream);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.GetMessage())));

        return stream.ToArray();
    }

    private static Compilation Mutate(Compilation compilation, Mutant mutant)
    {
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        SyntaxNode mutated = tree.GetRoot().ReplaceNode(mutant.OriginalNode, mutant.MutatedNode);

        return compilation.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(mutated, tree.Options));
    }
}
