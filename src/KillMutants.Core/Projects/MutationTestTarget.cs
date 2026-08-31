namespace KillMutants.Projects;

/// <summary>A project to mutate, paired with every test project that exercises it.</summary>
/// <param name="ProjectUnderTest">The project whose code is mutated.</param>
/// <param name="TestProjects">The projects whose tests decide whether a mutant is killed.</param>
/// <remarks>
/// A library is often exercised by more than one test suite. Mutants are generated once for the
/// project and the mutated assembly is injected into every one of those suites' output directories,
/// because a mutant is killed if <em>any</em> of them notices it. Treating each pair separately
/// would instead report the same mutation once per suite and count the ones other suites caught as
/// survivors.
/// </remarks>
public sealed record MutationTestTarget(
    ProjectUnderTest ProjectUnderTest,
    IReadOnlyList<TestProject> TestProjects)
{
    /// <summary>
    /// Every location the mutated assembly must be written to: next to each test assembly,
    /// replacing the copy that test host would otherwise load.
    /// </summary>
    /// <remarks>
    /// Nothing in a test project's references is rewritten, because the runtime simply loads
    /// whatever assembly sits beside the test application.
    /// </remarks>
    public IReadOnlyList<string> InjectionPaths =>
        [.. TestProjects.Select(test => Path.Combine(test.OutputDirectory, ProjectUnderTest.AssemblyFileName))];
}
