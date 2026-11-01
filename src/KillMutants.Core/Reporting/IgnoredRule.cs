namespace KillMutants.Reporting;

/// <summary>One rule of <c>--ignore-calls</c>, and how many mutants it silenced in this run.</summary>
/// <param name="Rule">The pattern, as the project wrote it.</param>
/// <param name="Mutants">How many mutants it silenced; zero for a rule that recognised nothing.</param>
/// <remarks>
/// Published whatever the count, because a rule with no effect is the one a project needs to see: a
/// method renamed, a pattern that never matched. Silently obeying it would be the tool lying by
/// omission about what the project asked (DEC0026).
/// </remarks>
public sealed record IgnoredRule(string Rule, int Mutants);
