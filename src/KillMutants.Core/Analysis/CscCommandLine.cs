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
    /// The files the command line embeds or links as resources, as full paths resolved against
    /// <paramref name="projectDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Read from the arguments themselves because Roslyn's parsed <see cref="Microsoft.CodeAnalysis.ResourceDescription"/>
    /// keeps the file to itself. A <c>.resx</c> reaches the compiler as the <c>.resources</c> MSBuild
    /// generated under <c>obj</c>, but any other <c>EmbeddedResource</c> - a JSON or text file - is
    /// passed as the file a person edits, so changing it changes the assembly. A series that ignored
    /// such a file would inherit verdicts reached on another one (DEC0025, review of #68).
    /// </remarks>
    public static IReadOnlyList<string> ResourceFiles(IReadOnlyList<string> arguments, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        List<string> files = [];

        foreach (string argument in arguments)
        {
            if (argument.Length < 2 || argument[0] is not ('/' or '-'))
            {
                continue;
            }

            int colon = argument.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0 ||
                argument[1..colon].ToUpperInvariant() is not ("RESOURCE" or "RES" or "LINKRESOURCE" or "LINKRES"))
            {
                continue;
            }

            // "file[,name[,public|private]]", the file quoted when it holds a comma or a space.
            string value = argument[(colon + 1)..];
            string file = value.StartsWith('"')
                ? value[1..Math.Max(1, value.IndexOf('"', 1))]
                : value.Split(',')[0];

            if (file.Length > 0)
            {
                files.Add(Path.GetFullPath(Path.Combine(projectDirectory, file)));
            }
        }

        return files;
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
