using System.IO.Enumeration;

namespace KillMutants.Filtering;

/// <summary>Decides which projects and source files a run leaves alone.</summary>
/// <remarks>
/// <para>
/// One filter covers both, because a user thinks in one rule: <em>ignore anything under here</em>.
/// A matching project is neither mutated nor used to run tests; a matching source file is still
/// compiled - dropping it would change the assembly - but never mutated.
/// </para>
/// <para>
/// Patterns are matched against the path relative to the search directory, always written with
/// <c>/</c> so the same pattern works on every platform, and matched without regard to case.
/// </para>
/// </remarks>
internal sealed class PathFilter
{
    private readonly string _root;
    private readonly string[] _patterns;

    private PathFilter(string root, string[] patterns)
    {
        _root = root;
        _patterns = patterns;
    }

    /// <summary>A filter that excludes nothing.</summary>
    public static PathFilter None { get; } = new(string.Empty, []);

    /// <summary>Builds a filter that excludes every path matching one of <paramref name="patterns"/>.</summary>
    /// <param name="root">The directory paths are made relative to before matching.</param>
    /// <param name="patterns">The patterns, or none to exclude nothing.</param>
    /// <exception cref="ArgumentException">A pattern is empty.</exception>
    public static PathFilter Excluding(string root, IEnumerable<string>? patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string[] listed = [.. patterns ?? []];

        if (listed.Length == 0)
        {
            return None;
        }

        foreach (string pattern in listed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern, nameof(patterns));
        }

        return new PathFilter(Path.GetFullPath(root), [.. listed.Select(Normalise)]);
    }

    /// <summary>True when <paramref name="path"/> is one the run must leave alone.</summary>
    public bool Excludes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_patterns.Length == 0)
        {
            return false;
        }

        string relative = Normalise(Path.GetRelativePath(_root, Path.GetFullPath(path)));

        return _patterns.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(pattern, relative, ignoreCase: true));
    }

    private static string Normalise(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
