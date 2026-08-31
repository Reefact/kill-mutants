namespace KillMutants.Execution;

/// <summary>
/// Takes custody of the assemblies the test hosts load, so mutants can be written over them and the
/// originals always put back.
/// </summary>
/// <remarks>
/// One project may be exercised by several test suites, each with its own copy of the assembly in
/// its own output directory. All of them are replaced together: a mutant has to be active
/// everywhere at once, or a suite would run against unmutated code and report a false survival.
/// </remarks>
internal sealed class AssemblyInjection : IDisposable
{
    private const string BackupSuffix = ".killmutants-original";

    private readonly IReadOnlyList<string> _targetPaths;
    private bool _restored;

    private AssemblyInjection(IReadOnlyList<string> targetPaths) => _targetPaths = targetPaths;

    /// <summary>Copies each assembly aside so it can be restored later.</summary>
    /// <exception cref="FileNotFoundException">One of the assemblies is not where it was expected.</exception>
    public static AssemblyInjection Protect(IReadOnlyList<string> targetPaths)
    {
        ArgumentNullException.ThrowIfNull(targetPaths);

        foreach (string targetPath in targetPaths)
        {
            RestoreAbandoned(targetPath);

            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException(
                    "The assembly to mutate was not found next to the test application. " +
                    "The test project may not have been built.",
                    targetPath);
            }

            File.Copy(targetPath, BackupFor(targetPath), overwrite: true);
        }

        return new AssemblyInjection(targetPaths);
    }

    /// <summary>
    /// Puts back an assembly left mutated by a run that was killed before it could clean up.
    /// </summary>
    /// <remarks>
    /// <see cref="Dispose"/> does not run on SIGKILL or a cancelled CI job, so a previous run can
    /// leave a mutated binary in the developer's output directory. Finding a backup here is proof
    /// that happened. Restoring it silently is right: the alternative is a developer whose tests
    /// fail for reasons they cannot see, and whose next KillMutants run would then take the mutated
    /// assembly as its baseline.
    /// </remarks>
    private static void RestoreAbandoned(string targetPath)
    {
        string backupPath = BackupFor(targetPath);

        if (!File.Exists(backupPath))
        {
            return;
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        File.Delete(backupPath);
    }

    /// <summary>Writes an assembly over every copy the test hosts will load.</summary>
    public void Inject(byte[] assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (string targetPath in _targetPaths)
        {
            File.WriteAllBytes(targetPath, assembly);
        }
    }

    /// <summary>Puts the original assemblies back.</summary>
    public void Dispose()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        foreach (string targetPath in _targetPaths)
        {
            RestoreAbandoned(targetPath);
        }
    }

    private static string BackupFor(string targetPath) => targetPath + BackupSuffix;
}
