namespace KillMutants.Reporting;

/// <summary>One rule of <c>--ignore-calls</c>, and how many mutants it silenced in this run.</summary>
/// <param name="Rule">The pattern, as the project wrote it.</param>
/// <param name="Mutants">
/// How many mutants it silences, a mutant another rule silences too included; zero for a rule under
/// which no mutant of this run falls.
/// </param>
/// <remarks>
/// Published whatever the count, because a rule with no effect is the one a project needs to see: a
/// method renamed, a pattern that never matched. Silently obeying it would be the tool lying by
/// omission about what the project asked. And every rule that silences a mutant is credited for it,
/// not the first in the list alone, so the count says what the rule does whatever its neighbours
/// (DEC0026, review of #74).
/// </remarks>
public sealed record IgnoredRule(string Rule, int Mutants);
