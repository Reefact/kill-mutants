using KillMutants.Projects;

namespace KillMutants.Selection;

/// <summary>Which of a project's source files a run generates mutants from.</summary>
/// <remarks>
/// <para>
/// Two shapes, and the difference between them is the whole of DEC0011's selection rule.
/// <see cref="Everything"/> is a full run, and also a project the change widened to; a file list is
/// a project only the changed lines of which are being judged.
/// </para>
/// <para>
/// Matched on the syntax tree's own path rather than on the directory a file sits in, which is not
/// a detail: a project can compile a file from anywhere - a <c>Compile</c> item with a
/// <c>Link</c>, a glob reaching out of the project folder, a generated file - and the compilation
/// is the only thing that knows for certain which files it was built from.
/// </para>
/// </remarks>
internal sealed class MutantSelection
{
    private readonly IReadOnlySet<string>? _files;

    private MutantSelection(IReadOnlySet<string>? files) => _files = files;

    /// <summary>Selects every file in the project.</summary>
    public static MutantSelection Everything { get; } = new(null);

    /// <summary>Selects only the named files, compared the way the filesystem would.</summary>
    public static MutantSelection Of(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return new MutantSelection(
            new HashSet<string>(files.Select(Path.GetFullPath), ProjectPaths.Comparer));
    }

    /// <summary>True when this selection takes in every file, so nothing needs matching.</summary>
    public bool IsEverything => _files is null;

    /// <summary>True when this selection takes in no file at all.</summary>
    public bool IsEmpty => _files is { Count: 0 };

    /// <summary>True when mutants may be generated from <paramref name="filePath"/>.</summary>
    public bool Includes(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return _files is null || _files.Contains(Path.GetFullPath(filePath));
    }
}
