using KillMutants.Projects;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Analysis;

/// <summary>
/// Turns the <c>csc</c> command line MSBuild reports into Roslyn's parsed representation.
/// </summary>
/// <remarks>
/// Nothing here is reconstructed or guessed: parse options, compilation options, references,
/// preprocessor symbols, language version and embedded resources all come from the arguments the
/// compiler was actually going to be given.
/// </remarks>
internal static class CscCommandLine
{
    /// <summary>Parses the arguments, rejecting a command line that cannot describe a real compilation.</summary>
    /// <exception cref="ProjectAnalysisException">The command line is empty or incomplete.</exception>
    public static CSharpCommandLineArguments Parse(IReadOnlyList<string> arguments, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Validate(arguments);

        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            arguments,
            baseDirectory: projectDirectory,
            sdkDirectory: null);

        if (parsed.Errors.Length > 0)
        {
            string errors = string.Join(Environment.NewLine, parsed.Errors.Select(error => error.ToString()));

            throw new ProjectAnalysisException(
                $"Could not read the compiler command line.{Environment.NewLine}{errors}");
        }

        if (parsed.SourceFiles.Length == 0)
        {
            throw new ProjectAnalysisException(
                "The compiler command line lists no source files, so there is nothing to mutate.");
        }

        return parsed;
    }

    /// <summary>
    /// Guards the known trap: when MSBuild considers a project up to date it can skip
    /// <c>CoreCompile</c> and report no arguments at all. Roslyn would then parse an empty command
    /// line into a perfectly valid default compilation, with no sources and no references, and every
    /// mutant compiled from it would fail for reasons that have nothing to do with the mutation.
    /// </summary>
    private static void Validate(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new ProjectAnalysisException(
                "MSBuild reported an empty compiler command line. The project may not have been built.");
        }

        if (!arguments.Any(argument => argument.StartsWith("/out:", StringComparison.Ordinal)) ||
            !arguments.Any(argument => argument.StartsWith("/target:", StringComparison.Ordinal)))
        {
            throw new ProjectAnalysisException(
                "The compiler command line reported by MSBuild is incomplete: it names no output or no target.");
        }
    }
}
