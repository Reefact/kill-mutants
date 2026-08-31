namespace KillMutants.Projects;

/// <summary>A project to mutate, paired with the test project that should catch the mutations.</summary>
/// <param name="ProjectUnderTest">The project whose code is mutated.</param>
/// <param name="TestProject">The project whose tests decide whether a mutant is killed.</param>
public sealed record MutationTestTarget(ProjectUnderTest ProjectUnderTest, TestProject TestProject)
{
    /// <summary>
    /// Where the mutated assembly must be written: next to the test assembly, replacing the copy the
    /// test host would otherwise load. Nothing in the test project's references is rewritten, because
    /// the runtime simply loads whatever assembly sits beside the test application.
    /// </summary>
    public string InjectionPath => Path.Combine(TestProject.OutputDirectory, ProjectUnderTest.AssemblyFileName);
}
