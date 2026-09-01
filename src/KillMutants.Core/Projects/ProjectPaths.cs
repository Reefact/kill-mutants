namespace KillMutants.Projects;

/// <summary>How two project paths are compared, which is a question about the filesystem.</summary>
/// <remarks>
/// <para>
/// Discovery matches a <c>ProjectReference</c> against the projects it found by enumerating
/// directories, and the two do not have to agree on case. On Windows and macOS they name the same
/// file whatever the case; comparing them ordinally there means a perfectly valid reference finds
/// nothing, and the project it names disappears from the run along with everything reachable only
/// through it - silently, since a reference that resolves to nothing is indistinguishable from one
/// pointing outside the tree.
/// </para>
/// <para>
/// The platform default is the rule, and it can be wrong in one direction: a case-sensitive volume
/// on macOS, where two paths differing only in case are two files, would have them treated as one.
/// That is the rarer mistake by a wide margin - a repository holding <c>Core.csproj</c> and
/// <c>core.csproj</c> side by side cannot be checked out on Windows at all - and it is the mistake
/// MSBuild itself makes, which keeps us consistent with the tool that resolves these references for
/// real.
/// </para>
/// </remarks>
internal static class ProjectPaths
{
    /// <summary>Compares two paths the way the filesystem this run is on would.</summary>
    public static StringComparer Comparer { get; } =
        ComparerFor(windows: OperatingSystem.IsWindows(), macOS: OperatingSystem.IsMacOS());

    /// <summary>
    /// The rule itself, with the platform passed in so it can be exercised on any of them.
    /// </summary>
    internal static StringComparer ComparerFor(bool windows, bool macOS) =>
        windows || macOS ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
