namespace KillMutants.Execution;

/// <summary>
/// Takes custody of the assembly the test host loads, so mutants can be written over it and the
/// original always put back.
/// </summary>
/// <remarks>
/// The original is copied aside on construction and restored on disposal, including when a run is
/// abandoned. Leaving a mutated binary in a developer's <c>bin</c> directory would be a genuinely
/// harmful side effect: their next test run would fail for reasons they could not see.
/// </remarks>
internal sealed class AssemblyInjection : IDisposable
{
    private const string BackupSuffix = ".killmutants-original";

    private readonly string _targetPath;
    private readonly string _backupPath;
    private bool _restored;

    private AssemblyInjection(string targetPath, string backupPath)
    {
        _targetPath = targetPath;
        _backupPath = backupPath;
    }

    /// <summary>Copies the assembly aside so it can be restored later.</summary>
    /// <exception cref="FileNotFoundException">The assembly is not where it was expected.</exception>
    public static AssemblyInjection Protect(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                $"The assembly to mutate was not found next to the test application. " +
                $"The test project may not have been built.",
                targetPath);
        }

        string backupPath = targetPath + BackupSuffix;
        File.Copy(targetPath, backupPath, overwrite: true);

        return new AssemblyInjection(targetPath, backupPath);
    }

    /// <summary>Writes an assembly over the one the test host will load.</summary>
    public void Inject(byte[] assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        File.WriteAllBytes(_targetPath, assembly);
    }

    /// <summary>Puts the original assembly back.</summary>
    public void Dispose()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        if (File.Exists(_backupPath))
        {
            File.Copy(_backupPath, _targetPath, overwrite: true);
            File.Delete(_backupPath);
        }
    }
}
