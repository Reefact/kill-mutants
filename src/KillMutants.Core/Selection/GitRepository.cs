using System.Formats.Tar;
using KillMutants.Processes;

namespace KillMutants.Selection;

/// <summary>
/// The git working copy a partial run reads its change from.
/// </summary>
/// <remarks>
/// <para>
/// Git is used as a command-line contract, the same way xUnit is: no LibGit2Sharp, no managed
/// reimplementation of the object database. A partial run asks git four questions - resolve this
/// revision, find the common ancestor, list what changed, hand me the base tree - and every one of
/// them has a stable plumbing command that answers it exactly.
/// </para>
/// <para>
/// Every failure is turned into a <see cref="ChangeSelectionException"/> with git's own message
/// attached. The messages matter more here than elsewhere: the most common failure by far is a base
/// revision that is not in the clone at all, which is what a CI job's shallow checkout produces, and
/// a reader who is told only "could not resolve" goes looking at their branch name instead.
/// </para>
/// </remarks>
internal sealed class GitRepository
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private GitRepository(string root) => Root = root;

    /// <summary>The top of the working copy, which every path git reports is relative to.</summary>
    public string Root { get; }

    /// <summary>Finds the working copy <paramref name="directory"/> sits in.</summary>
    /// <exception cref="ChangeSelectionException">It is not in one, or git is not installed.</exception>
    public static async Task<GitRepository> ContainingAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string root = await RunAsync(
                directory,
                ["rev-parse", "--show-toplevel"],
                $"'{directory}' is not inside a git working copy, so there is no change to run since.",
                cancellationToken)
            .ConfigureAwait(false);

        return new GitRepository(Path.GetFullPath(root));
    }

    /// <summary>Resolves a revision - a branch, a tag, a sha - to the commit it names.</summary>
    /// <exception cref="ChangeSelectionException">Nothing of that name is in this clone.</exception>
    public async Task<string> ResolveAsync(string revision, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        return await RunAsync(
                Root,
                ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"],
                $"'{revision}' does not name a commit in this clone. A shallow checkout often does " +
                "not contain the branch you are comparing against: fetch it first, or check out " +
                "with the full history.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The commit the change forked from, which is what "since" means.
    /// </summary>
    /// <remarks>
    /// The merge base rather than the branch tip. Comparing against the tip would report every
    /// commit made on the other branch since the fork as part of this change - reversed, since the
    /// diff runs the other way - so a branch that is merely out of date would select files nobody
    /// on it has touched.
    /// </remarks>
    /// <exception cref="ChangeSelectionException">The two commits share no ancestor.</exception>
    public async Task<string> MergeBaseAsync(
        string revision,
        string head,
        CancellationToken cancellationToken = default) =>
        await RunAsync(
                Root,
                ["merge-base", revision, head],
                $"'{revision}' and '{head}' share no common ancestor, so there is no point to " +
                "measure the change from.",
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Reads the change between <paramref name="baseRevision"/> and the working tree.</summary>
    /// <remarks>
    /// Untracked files are listed separately and counted as additions. They are not in the diff, and
    /// leaving them out would hide the sharpest case of all: a brand new source file, never
    /// committed, whose code is compiled into the assembly this run mutates.
    /// </remarks>
    public async Task<IReadOnlyList<FileChange>> ChangesSinceAsync(
        string baseRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRevision);

        // --no-renames on purpose. Git reports a rename by its destination alone, and the source is
        // exactly what the selection needs: moving a test file away removes the coverage it
        // provided, and the destination says nothing about that. See DEC0011.
        string diff = await RunRawAsync(
                Root,
                ["diff", "--no-renames", "--name-status", "-z", baseRevision],
                $"The change since '{baseRevision}' could not be read.",
                cancellationToken)
            .ConfigureAwait(false);

        string untracked = await RunRawAsync(
                Root,
                ["ls-files", "--others", "--exclude-standard", "-z"],
                "The untracked files could not be listed.",
                cancellationToken)
            .ConfigureAwait(false);

        List<FileChange> changes = [.. ReadNameStatus(diff)];

        changes.AddRange(Split(untracked).Select(path => new FileChange(Absolute(path), ChangeKind.Added)));

        return changes;
    }

    /// <summary>True when the tree about to be built is not exactly what <c>HEAD</c> points at.</summary>
    /// <remarks>
    /// Recorded rather than refused. A local run on a dirty tree is a perfectly reasonable thing to
    /// do - it is what the feature is for, half the time - but a report naming a commit while
    /// describing a tree that is not that commit would be a report nobody can reproduce.
    /// </remarks>
    public async Task<bool> HasUncommittedChangesAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(
            await RunRawAsync(
                    Root,
                    ["status", "--porcelain", "-z"],
                    "The state of the working tree could not be read.",
                    cancellationToken)
                .ConfigureAwait(false));

    /// <summary>Every file tracked at <paramref name="revision"/>, by repository path.</summary>
    /// <remarks>
    /// One cheap call that decides whether the base tree needs exporting at all: a change whose files
    /// all sit in projects that still exist has nothing to ask the base revision that the head graph
    /// cannot answer.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListFilesAsync(
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        string listing = await RunRawAsync(
                Root,
                ["ls-tree", "-r", "--name-only", "-z", revision],
                $"The files at '{revision}' could not be listed.",
                cancellationToken)
            .ConfigureAwait(false);

        return [.. Split(listing)];
    }

    /// <summary>The submodule paths a revision records, as repository paths.</summary>
    /// <remarks>
    /// A submodule is a gitlink: one entry of mode <c>160000</c> naming a commit in another
    /// repository. <c>ls-tree -r</c> does not descend into it - it cannot, the objects are not here -
    /// so a listing of file names contains <c>libs/Core</c> and nothing beneath it. Measured:
    /// <code>
    /// $ git ls-tree -r HEAD
    /// 100644 blob e25f1814…  Root.csproj
    /// 160000 commit 0013cc50…  libs/Core
    /// </code>
    /// That is why a path inside a submodule is missing from a file listing and from a
    /// <c>git archive</c> alike, and why checking exact tracked names cannot notice its absence.
    /// Review found the refusal that name check was supposed to raise never firing.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListSubmodulePathsAsync(
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        string listing = await RunRawAsync(
                Root,
                ["ls-tree", "-r", "-z", revision],
                $"The tree at '{revision}' could not be listed.",
                cancellationToken)
            .ConfigureAwait(false);

        List<string> gitlinks = [];

        // "<mode> SP <type> SP <object> TAB <path>", one record per NUL.
        foreach (string record in Split(listing))
        {
            int tab = record.IndexOf('\t', StringComparison.Ordinal);

            if (tab > 0 && record.StartsWith("160000 ", StringComparison.Ordinal))
            {
                gitlinks.Add(record[(tab + 1)..]);
            }
        }

        return gitlinks;
    }

    /// <summary>Lays the code of <paramref name="revision"/> out in its own directory.</summary>
    /// <remarks>
    /// <para>
    /// A worktree, not <c>git archive</c>, and the difference is the whole point. An archive is a
    /// <em>distribution</em>: it honours <c>export-ignore</c>, because a release tarball is not
    /// meant to carry the tests, and it writes a submodule as an empty directory because those
    /// objects belong to another repository. A worktree is a <em>checkout</em>: it puts the code
    /// back as it was. Measured on this repository - the archive left out a tracked
    /// <c>Directory.Build.props</c> and the whole of a submodule; the worktree left out neither, in
    /// 0.07 s against 6.06 s.
    /// </para>
    /// <para>
    /// The first version of this chose the archive deliberately, because a worktree registers itself
    /// inside the user's repository and has to be removed again. That cost is real and is paid here
    /// - it is why this returns something disposable rather than a path - but it was weighed against
    /// a benefit that had never been measured, and the archive was silently returning less code than
    /// the revision holds.
    /// </para>
    /// <para>
    /// Submodules are laid out one at a time, each from its own object store under
    /// <c>.git/modules</c>. Not <c>git submodule update</c>, which re-clones from the recorded URL:
    /// that needs the network, and refuses a local path outright since git closed CVE-2022-39253.
    /// Asking the submodule's own repository for a worktree needs neither.
    /// </para>
    /// </remarks>
    public async Task<ICodeSnapshot> OpenCodeBeforeAsync(
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        string destination = Path.Combine(Path.GetTempPath(), $"killmutants-base-{Guid.NewGuid():N}");

        await RunRawAsync(
                Root,
                ["worktree", "add", "--quiet", "--detach", destination, revision],
                $"The code at '{revision}' could not be laid out for reading.",
                cancellationToken)
            .ConfigureAwait(false);

        List<string> laidOut = [];
        List<string> missing = [];

        try
        {
            foreach (string path in await ListSubmodulePathsAsync(revision, cancellationToken)
                         .ConfigureAwait(false))
            {
                if (await LayOutSubmoduleAsync(revision, path, destination, cancellationToken)
                        .ConfigureAwait(false))
                {
                    laidOut.Add(path);
                }
                else
                {
                    missing.Add(path);
                }
            }
        }
        catch
        {
            new Worktree(this, destination, laidOut, missing).Dispose();

            throw;
        }

        return new Worktree(this, destination, laidOut, missing);
    }

    /// <summary>
    /// Lays one submodule out from its own object store, and says whether it could.
    /// </summary>
    /// <remarks>
    /// A submodule the user never initialised has no objects here to lay out, and no amount of
    /// local work will produce them. That is a snapshot that is incomplete rather than a run that
    /// has failed: the caller reports what it could not compare instead of claiming it did.
    /// </remarks>
    private async Task<bool> LayOutSubmoduleAsync(
        string revision,
        string path,
        string destination,
        CancellationToken cancellationToken)
    {
        // The conventional layout: `git submodule add` names the module after its path. A module
        // named otherwise is not found here, and is reported missing rather than guessed at.
        string moduleDirectory = Path.Combine(Root, ".git", "modules", path.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(moduleDirectory))
        {
            return false;
        }

        try
        {
            string commit = (await RunRawAsync(
                    Root,
                    ["rev-parse", $"{revision}:{path}"],
                    $"The commit recorded for '{path}' could not be read.",
                    cancellationToken)
                .ConfigureAwait(false)).Trim();

            await RunRawAsync(
                    moduleDirectory,
                    ["worktree", "add", "--quiet", "--detach", Path.Combine(destination, path), commit],
                    $"The code of '{path}' could not be laid out for reading.",
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (ChangeSelectionException)
        {
            return false;
        }
    }

    /// <summary>A worktree, and every worktree opened beneath it, removed together.</summary>
    private sealed class Worktree(
        GitRepository repository,
        string root,
        IReadOnlyList<string> submodules,
        IReadOnlyList<string> missing) : ICodeSnapshot
    {
        public string Root { get; } = root;

        public IReadOnlyList<string> Missing { get; } = missing;

        /// <remarks>
        /// Submodules first: each is a worktree of a different repository, and removing the parent
        /// would leave their registrations behind, pointing at a directory that no longer exists.
        /// Failures are swallowed on purpose - this runs while the run is ending, possibly because
        /// of an error, and `git worktree prune` clears whatever is left whenever git next looks.
        /// </remarks>
        public void Dispose()
        {
            foreach (string path in submodules)
            {
                string moduleDirectory = Path.Combine(
                    repository.Root, ".git", "modules", path.Replace('/', Path.DirectorySeparatorChar));

                Remove(moduleDirectory, Path.Combine(Root, path));
            }

            Remove(repository.Root, Root);
            Scratch.DeleteDirectory(Root);
        }

        private static void Remove(string gitDirectory, string worktree)
        {
            try
            {
                GitRepository
                    .RunRawAsync(gitDirectory, ["worktree", "remove", "--force", worktree], "", default)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is ChangeSelectionException or IOException)
            {
                // Left for `git worktree prune`.
            }
        }
    }


    /// <summary>Reads git's <c>--name-status -z</c> output: a status, then a path, then repeat.</summary>
    private IEnumerable<FileChange> ReadNameStatus(string output)
    {
        string[] fields = [.. Split(output)];

        for (int index = 0; index + 1 < fields.Length; index += 2)
        {
            yield return new FileChange(Absolute(fields[index + 1]), KindOf(fields[index]));
        }
    }

    /// <summary>
    /// Reads one of git's status letters, conservatively.
    /// </summary>
    /// <remarks>
    /// Only <c>A</c> is read as an addition, and only <c>D</c> as a deletion, because those two are
    /// the ones the selection treats specially - an added test file does not widen anything, a
    /// deleted one does. Everything else, including the letters this tool never asks for, is read as
    /// a modification: that is the reading that widens rather than narrows, so an unfamiliar status
    /// costs time and never silence.
    /// </remarks>
    private static ChangeKind KindOf(string status) => status switch
    {
        "A" => ChangeKind.Added,
        "D" => ChangeKind.Deleted,
        _ => ChangeKind.Modified,
    };

    private static string[] Split(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private string Absolute(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    private static async Task<string> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string whenItFails,
        CancellationToken cancellationToken) =>
        (await RunRawAsync(workingDirectory, arguments, whenItFails, cancellationToken)
            .ConfigureAwait(false))
        .Trim();

    private static async Task<string> RunRawAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string whenItFails,
        CancellationToken cancellationToken)
    {
        ProcessResult result;

        try
        {
            result = await ProcessRunner
                .RunAsync("git", arguments, workingDirectory, Budget, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            throw new ChangeSelectionException(
                $"KillMutants could not run git, which --since needs: {exception.Message}", exception);
        }

        if (result.TimedOut)
        {
            throw new ChangeSelectionException($"{whenItFails} git did not finish in time.");
        }

        if (!result.Succeeded)
        {
            // git's own message, kept: it names the ref, the shallow clone or the missing object far
            // more precisely than anything this tool could say on its behalf.
            string detail = result.CombinedOutput.Trim();

            throw new ChangeSelectionException(
                detail.Length == 0 ? whenItFails : $"{whenItFails}{Environment.NewLine}{detail}");
        }

        return result.StandardOutput;
    }
}
