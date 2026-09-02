using System.Diagnostics;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Turns a copied fixture into a git working copy, so a partial run has two revisions to compare.
/// </summary>
/// <remarks>
/// Real git, not a stub. <c>--since</c> is a contract with git's plumbing - what
/// <c>--name-status -z</c> emits, what <c>merge-base</c> answers, what <c>archive</c> produces - and
/// a fake that agrees with our reading of it would pin the reading rather than the behaviour. The
/// repository is created inside the throwaway fixture, so nothing here touches the real one.
/// </remarks>
internal static class FixtureRepository
{
    /// <summary>Creates a repository at <paramref name="root"/> with everything committed.</summary>
    public static void InitialiseAt(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // Build output is not committed and must not be: a fixture is copied without bin and obj,
        // and a run creates them. Left untracked they would show up as an added file in every diff.
        File.WriteAllText(
            Path.Combine(root, ".gitignore"),
            string.Join(Environment.NewLine, "bin/", "obj/", string.Empty));

        // -b names the branch rather than leaving it to the machine's init.defaultBranch, and the
        // identity is passed per command so the runner needs no global git configuration.
        Run(root, "init", "-b", "main");
        CommitAll(root, "the base revision");
    }

    /// <summary>Commits everything currently in the tree.</summary>
    public static void CommitAll(string root, string message)
    {
        Run(root, "add", "-A");
        Run(
            root,
            "-c", "user.name=KillMutants tests",
            "-c", "user.email=tests@example.invalid",
            "commit", "-q", "--allow-empty", "-m", message);
    }

    /// <summary>Adds <paramref name="source"/> as a submodule at <paramref name="path"/>.</summary>
    /// <remarks>
    /// <c>protocol.file.allow=always</c> is required: git has refused the file transport for
    /// submodules by default since 2.38 (CVE-2022-39253), and a fixture repository beside another on
    /// the same disk is exactly that transport. It is passed per command rather than configured, so
    /// nothing outside this process is loosened.
    /// </remarks>
    public static void AddSubmodule(string root, string source, string path)
    {
        Run(root, "-c", "protocol.file.allow=always", "submodule", "add", "-q", source, path);
        CommitAll(root, $"add {path} as a submodule");
    }

    /// <summary>
    /// Moves the submodule's checkout on, leaving the outer repository's gitlink uncommitted.
    /// </summary>
    /// <remarks>
    /// Deliberately not committed. The first version of this committed the bump, which left nothing
    /// between the outer repository's HEAD and its working tree - so the test measured an empty diff
    /// rather than a submodule change, and failed for a reason that had nothing to do with what it
    /// was written to check.
    /// </remarks>
    public static void BumpSubmodule(string root, string path)
    {
        Run(Path.Combine(root, path), "-c", "protocol.file.allow=always", "fetch", "-q", "origin");
        Run(Path.Combine(root, path), "checkout", "-q", "origin/main");
    }

    private static void Run(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed in '{root}':" +
                $"{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }
}
