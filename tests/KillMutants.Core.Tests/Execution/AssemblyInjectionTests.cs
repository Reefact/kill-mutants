using KillMutants.Execution;

namespace KillMutants.Core.Tests.Execution;

public class AssemblyInjectionTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"killmutants-injection-{Guid.NewGuid():N}");

    public AssemblyInjectionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteAssembly(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void Every_copy_is_replaced_and_every_original_restored()
    {
        // One library exercised by two suites has a copy in each suite's output directory. A mutant
        // must be active in both, or a suite runs against unmutated code and reports a false survival.
        string first = WriteAssembly("first.dll", "original");
        string second = WriteAssembly("second.dll", "original");

        using (var injection = AssemblyInjection.Protect([first, second]))
        {
            injection.Inject("mutated"u8.ToArray());

            Assert.Equal("mutated", File.ReadAllText(first));
            Assert.Equal("mutated", File.ReadAllText(second));
        }

        Assert.Equal("original", File.ReadAllText(first));
        Assert.Equal("original", File.ReadAllText(second));
    }

    [Fact]
    public void No_backup_is_left_behind()
    {
        string assembly = WriteAssembly("lib.dll", "original");

        using (var injection = AssemblyInjection.Protect([assembly]))
        {
            injection.Inject("mutated"u8.ToArray());
        }

        Assert.Equal(["lib.dll"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    /// <summary>
    /// RB-006. Disposal does not run on SIGKILL or a cancelled CI job, so a previous run can leave a
    /// mutated assembly behind. Finding a backup proves that happened, and restoring it matters
    /// twice over: the developer's own tests would otherwise fail for reasons they cannot see, and
    /// the next KillMutants run would take the mutated assembly as its baseline.
    /// </summary>
    [Fact]
    public void An_assembly_abandoned_by_a_killed_run_is_restored_before_anything_else()
    {
        string assembly = WriteAssembly("lib.dll", "mutated-and-abandoned");
        WriteAssembly("lib.dll.killmutants-original", "original");

        using (AssemblyInjection.Protect([assembly]))
        {
            Assert.Equal("original", File.ReadAllText(assembly));
        }

        Assert.Equal("original", File.ReadAllText(assembly));
        Assert.Equal(["lib.dll"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void A_missing_assembly_is_reported_rather_than_silently_skipped()
    {
        Assert.Throws<FileNotFoundException>(
            () => AssemblyInjection.Protect([Path.Combine(_directory, "absent.dll")]));
    }
}
