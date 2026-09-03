using KillMutants.Projects;

namespace KillMutants.Selection;

/// <summary>
/// Paths as a root names them: relative to it, and written with <c>/</c>.
/// </summary>
/// <remarks>
/// A partial run compares two trees that are not in the same place on disk - the working copy and
/// the earlier state laid out beside it - so a project can only be recognised across them by the
/// name each root gives it. Everything else about the two paths differs.
/// </remarks>
internal static class RelativePath
{
    /// <summary>How two relative paths are compared: the filesystem's rule, as everywhere else.</summary>
    public static StringComparer Comparer => ProjectPaths.Comparer;

    /// <summary>The relative name of a path, or null when it is outside <paramref name="root"/>.</summary>
    public static string? Of(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));

        // GetRelativePath climbs out with "..", and returns the path unchanged when the two are on
        // different volumes. Both mean the same thing here: not under this root.
        //
        // The parent segment, not the prefix: review pointed out that a directory legally named
        // "..tests" starts with those two characters without climbing anywhere, and was being read
        // as outside the root.
        return ClimbsOut(relative) || Path.IsPathRooted(relative)
            ? null
            : Normalise(relative);
    }

    /// <summary>True when the first segment of a relative path is the parent directory.</summary>
    private static bool ClimbsOut(string relative)
    {
        if (!relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        return relative.Length == 2 ||
               relative[2] == Path.DirectorySeparatorChar ||
               relative[2] == Path.AltDirectorySeparatorChar;
    }

    /// <summary>The absolute path a relative name points at inside <paramref name="root"/>.</summary>
    public static string In(string root, string relative) =>
        Path.GetFullPath(Path.Combine(root, relative));

    /// <summary>The directory part of a relative name, or the empty string at the top.</summary>
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
