using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A series inherits verdicts only on the same code with the same settings and tool (DEC0015), and
/// the identity is what decides it - so every difference it is meant to see has to be seen.
/// </summary>
public class RunIdentityTests
{
    private static RunIdentity Identity(
        CodeState? code = null,
        string tool = "0.1.0-alpha",
        string configuration = "Release",
        bool coverage = true,
        string[]? mutators = null,
        string[]? without = null,
        string[]? exclude = null,
        PlannedScope? scope = null,
        string? fingerprint = null,
        string[]? graph = null,
        string? framework = null) => new(
        code ?? new CodeState("0123abcd", IsExact: true),
        tool,
        configuration,
        coverage,
        mutators ?? ["Comparison", "Negation"],
        without ?? [],
        exclude ?? ["tests/fixtures/*"],
        scope,
        fingerprint,
        graph,
        framework);

    [Fact]
    public void The_same_identity_continues()
    {
        Assert.Null(Identity().WhyNotContinuedBy(Identity()));
    }

    [Fact]
    public void Lists_are_compared_as_sets()
    {
        Assert.Null(Identity(mutators: ["Negation", "Comparison"]).WhyNotContinuedBy(Identity()));
    }

    [Fact]
    public void A_different_commit_does_not_continue()
    {
        string? why = Identity(code: new CodeState("ffffffff", true)).WhyNotContinuedBy(Identity());

        Assert.Contains("0123abcd", why, StringComparison.Ordinal);
        Assert.Contains("ffffffff", why, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two services of one repository with the same layout describe themselves identically from the
    /// commit down - the keys and the graph are relative to the directory the run was pointed at -
    /// so the directory has to be part of what is compared, or one would inherit the other's verdicts.
    /// </summary>
    [Fact]
    public void Another_directory_of_the_same_commit_does_not_continue()
    {
        RunIdentity first = Identity(code: new CodeState("0123abcd", IsExact: true, "services/first"));
        RunIdentity second = Identity(code: new CodeState("0123abcd", IsExact: true, "services/second"));

        string? why = second.WhyNotContinuedBy(first);

        Assert.Contains("'services/first'", why, StringComparison.Ordinal);
        Assert.Contains("'services/second'", why, StringComparison.Ordinal);
        Assert.Null(second.WhyNotContinuedBy(second));
    }

    [Fact]
    public void The_top_of_the_working_copy_is_named_as_such()
    {
        RunIdentity top = Identity(code: new CodeState("0123abcd", IsExact: true));
        RunIdentity below = Identity(code: new CodeState("0123abcd", IsExact: true, "src"));

        Assert.Contains("the top", below.WhyNotContinuedBy(top), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tree_with_uncommitted_changes_never_continues_on_either_side()
    {
        RunIdentity dirty = Identity(code: new CodeState("0123abcd", IsExact: false));

        Assert.Contains("uncommitted", Identity().WhyNotContinuedBy(dirty), StringComparison.Ordinal);
        Assert.Contains("uncommitted", dirty.WhyNotContinuedBy(Identity()), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_code_state_never_continues_on_either_side()
    {
        RunIdentity unknown = Identity() with { Code = null };

        Assert.NotNull(Identity().WhyNotContinuedBy(unknown));
        Assert.NotNull(unknown.WhyNotContinuedBy(Identity()));
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("configuration")]
    [InlineData("coverage")]
    [InlineData("mutators")]
    [InlineData("without")]
    [InlineData("exclude")]
    public void A_setting_that_changes_verdicts_does_not_continue(string differing)
    {
        RunIdentity changed = differing switch
        {
            "tool" => Identity(tool: "0.2.0"),
            "configuration" => Identity(configuration: "Debug"),
            "coverage" => Identity(coverage: false),
            "mutators" => Identity(mutators: ["Comparison"]),
            "without" => Identity(without: ["StringLiteral"]),
            _ => Identity(exclude: []),
        };

        Assert.NotNull(changed.WhyNotContinuedBy(Identity()));
    }

    /// <summary>What the run only learns once it has selected its mutants is compared only then.</summary>
    [Fact]
    public void What_one_side_does_not_know_yet_is_not_compared()
    {
        RunIdentity known = Identity(
            scope: new PlannedScope("full", null, null, true),
            fingerprint: "abc",
            graph: ["Core <- Core.Tests"],
            framework: "xUnit 4.0.0");

        Assert.Null(Identity().WhyNotContinuedBy(known));
        Assert.Null(known.WhyNotContinuedBy(Identity()));
    }

    [Fact]
    public void A_different_population_does_not_continue()
    {
        Assert.Contains(
            "mutants",
            Identity(fingerprint: "abc").WhyNotContinuedBy(Identity(fingerprint: "def")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_project_graph_does_not_continue()
    {
        Assert.NotNull(
            Identity(graph: ["Core <- Core.Tests"])
                .WhyNotContinuedBy(Identity(graph: ["Core <- Core.Tests, Domain.Tests"])));
    }

    [Fact]
    public void A_different_test_framework_does_not_continue()
    {
        Assert.Contains(
            "xUnit 4.0.1",
            Identity(framework: "xUnit 4.0.1").WhyNotContinuedBy(Identity(framework: "xUnit 4.0.0")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_fingerprint_ignores_order_and_repetition()
    {
        string once = RunIdentity.FingerprintOf(["b", "a", "c"]);

        Assert.Equal(once, RunIdentity.FingerprintOf(["a", "b", "c"]));
        Assert.Equal(once, RunIdentity.FingerprintOf(["c", "a", "b", "a"]));
        Assert.NotEqual(once, RunIdentity.FingerprintOf(["a", "b"]));
        Assert.Equal(64, once.Length);
    }
}
