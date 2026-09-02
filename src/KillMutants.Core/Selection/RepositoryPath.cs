using KillMutants.Projects;

namespace KillMutants.Selection;

/// <summary>
/// Paths as a repository names them: relative to its root and written with <c>/</c>.
/// </summary>
/// <remarks>
/// A partial run compares two trees that are not in the same place on disk - the working copy and
/// the base revision exported beside it - so a project can only be recognised across them by the
/// name the repository gives it. Everything else about the two paths differs.
/// </remarks>
internal static class RepositoryPath
{
    /// <summary>How two repository paths are compared: the filesystem's rule, as everywhere else.</summary>
    public static StringComparer Comparer => ProjectPaths.Comparer;

    /// <summary>The repository name of a path, or null when it is outside <paramref name="root"/>.</summary>
    public static string? Of(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));

        // GetRelativePath climbs out with "..", and returns the path unchanged when the two are on
        // different volumes. Both mean the same thing here: not in this repository.
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : Normalise(relative);
    }

    /// <summary>The absolute path a repository name points at inside <paramref name="root"/>.</summary>
    public static string In(string root, string relative) =>
        Path.GetFullPath(Path.Combine(root, relative));

    /// <summary>The directory part of a repository name, or the empty string at the top.</summary>
    public static string DirectoryOf(string relative)
    {
        int slash = relative.LastIndexOf('/');

        return slash < 0 ? string.Empty : relative[..slash];
    }

    /// <summary>
    /// True when <paramref name="path"/> sits in <paramref name="directory"/> or beneath it.
    /// </summary>
    /// <remarks>
    /// Whole segments only. A prefix comparison would put <c>src/CoreTests/Thing.cs</c> inside
    /// <c>src/Core</c>, which is how a change to one project comes to be attributed to another.
    /// </remarks>
    public static bool IsUnder(string path, string directory) =>
        directory.Length == 0 ||
        (path.Length > directory.Length &&
         path[directory.Length] == '/' &&
         path.AsSpan(0, directory.Length).Equals(directory, StringComparisonOf(Comparer)));

    private static string Normalise(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static StringComparison StringComparisonOf(StringComparer comparer) =>
        ReferenceEquals(comparer, StringComparer.Ordinal)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
}
