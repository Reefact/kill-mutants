namespace KillMutants.Reporting;

/// <summary>A project a partial run mutated whole, and the changes that made it do so.</summary>
/// <param name="Project">
/// The project file, relative to the root the change was read from - the way the diff names it.
/// </param>
/// <param name="Because">
/// The changed files that widened it, named the same way: an existing test its suites are built
/// from, its own project file, a build file above it, a generator it consumes.
/// </param>
/// <remarks>
/// Metadata about the run, like <see cref="RunScope"/>, and never a status a mutant carries.
/// DEC0010 refuses a state meaning "outside the diff" because it leaves the denominator; a widened
/// project's mutants stay in the selection and in the verdict, and this only says why they are there.
/// </remarks>
public sealed record WidenedProject(string Project, IReadOnlyList<string> Because);
