using Microsoft.CodeAnalysis;

namespace KillMutants.Analysis;

/// <summary>What a source-generator run produced.</summary>
/// <param name="Compilation">The compilation with the generated code added.</param>
/// <param name="Failure">
/// Why the result cannot be trusted, or null when every generator ran. Carried rather than thrown:
/// reconstructing the baseline must stop the run, while a mutant whose generators failed is one
/// KillMutants cannot judge - which is a verdict of its own, not an abort.
/// </param>
internal sealed record GeneratedCompilation(Compilation Compilation, string? Failure);
