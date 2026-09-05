using KillMutants.Processes;

using KillMutants.Selection;

namespace KillMutants.Git;

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
                ["diff", "--no-renames", "--raw", "-z", baseRevision],
                $"The change since '{baseRevision}' could not be read.",
                cancellationToken)
            .ConfigureAwait(false);

        string untracked = await RunRawAsync(
                Root,
                ["ls-files", "--others", "--exclude-standard", "-z"],
                "The untracked files could not be listed.",
                cancellationToken)
            .ConfigureAwait(false);

        List<FileChange> changes = [.. ReadRaw(diff)];

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
    public Task<IReadOnlyList<string>> ListSubmodulePathsAsync(
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        return ListSubmodulePathsAsync([], revision, cancellationToken);
    }

    /// <summary>The same listing, of any repository's tree - a submodule's own included.</summary>
    /// <remarks>
    /// Taking the repository as an argument is what lets the layout recurse. A gitlink recorded
    /// <em>inside</em> a submodule is invisible to the parent's listing for the reason above, so
    /// enumerating once from the parent found the first level and stopped - and review found the
    /// consequence: the levels below were neither laid out nor reported, and the snapshot claimed to
    /// be complete while a whole subtree of it was an empty directory.
    /// </remarks>
    private async Task<IReadOnlyList<string>> ListSubmodulePathsAsync(
        IReadOnlyList<string> level,
        string revision,
        CancellationToken cancellationToken)
    {
        string listing = await RunRawAsync(
                Root,
                [.. level, "ls-tree", "-r", "-z", revision],
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

        List<Submodule> laidOut = [];
        List<string> missing = [];

        // Inside the try that removes it, and review found it outside. Measured what git leaves when
        // it is killed part way through - the caller cancels, or the budget expires on a large
        // checkout: a registration carrying `locked = initializing`, which `git worktree prune` does
        // not clear and which a single `--force` refuses to remove. Those would have accumulated in
        // the user's own repository, one per attempt, each with a partial checkout beside it.
        try
        {
            await RunRawAsync(
                    Root,
                    ["worktree", "add", "--quiet", "--detach", destination, revision],
                    $"The code at '{revision}' could not be laid out for reading.",
                    cancellationToken)
                .ConfigureAwait(false);

            await LayOutComponentsAsync(
                    [], revision, string.Empty, destination, laidOut, missing, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            new Worktree(this, destination, laidOut, missing).Dispose();

            throw;
        }

        return new Worktree(this, destination, laidOut, missing);
    }

    /// <summary>Where a submodule's own objects live, or null when they are not here.</summary>
    /// <remarks>
    /// Asked of git rather than assembled from the path, and review found why that matters.
    /// <c>.git/modules/&lt;path&gt;</c> is the conventional layout and not a rule: measured, after
    /// <c>git mv libs/Old libs/Core</c> the gitlink moves to the new path while the object store
    /// stays at <c>.git/modules/libs/Old</c>, so an ordinary rename made this report a submodule
    /// missing whose working tree is fully populated. A run started inside a <c>git worktree</c> has
    /// no <c>.git</c> directory at all, and there every gitlink was reported missing.
    /// <para>
    /// The comparison against the outer repository is what keeps the answer honest. git walks
    /// upwards, so asking an uninitialised submodule's empty directory answers with the parent's own
    /// git directory - measured. Equal means this path is not a repository of its own, which is the
    /// one case the caller reports as missing rather than fails on: those objects are not here, and
    /// no amount of local work will produce them.
    /// </para>
    /// </remarks>
    private async Task<string?> ModuleDirectoryOfAsync(
        string path,
        string name,
        IReadOnlyList<string> level,
        string revision,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(directory))
        {
            // Not there to be asked, which for a component the change *removed* is the ordinary
            // case rather than an absent one. Review found the run passing green over a deleted
            // component for exactly this: asking the working tree where a thing is, when the thing
            // being read is what the working tree no longer has.
            return await StoreOfRemovedAsync(level, revision, name, cancellationToken)
                .ConfigureAwait(false);
        }

        string outer = await RunAsync(
                Root,
                [.. level, "rev-parse", "--absolute-git-dir"],
                "The repository's own git directory could not be read.",
                cancellationToken)
            .ConfigureAwait(false);

        string inner;

        try
        {
            inner = await RunAsync(
                    directory,
                    ["rev-parse", "--absolute-git-dir"],
                    $"The git directory of '{path}' could not be read.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChangeSelectionException)
        {
            return null;
        }

        return SamePath(inner, outer) ? null : inner;
    }

    /// <summary>Where git keeps a component's objects, found without asking the working tree.</summary>
    /// <remarks>
    /// <para>
    /// By <em>name</em> rather than by path, because those are not the same thing and only the name
    /// says where the store is: git keeps it at <c>&lt;git dir&gt;/modules/&lt;name&gt;</c>, and the
    /// name comes from <c>.gitmodules</c> <em>at the revision being read</em> - the one place that
    /// still describes the component after a change removed it. Assembling the path from the
    /// gitlink was wrong for a renamed component and is still wrong; reading the name is not.
    /// </para>
    /// <para>
    /// Answering null here means the objects genuinely are not present - a clone that never
    /// initialised the component, then deleted it - and the caller reports the component missing
    /// rather than laying out an empty directory the base graph would read as an answer.
    /// </para>
    /// </remarks>
    private async Task<string?> StoreOfRemovedAsync(
        IReadOnlyList<string> level,
        string revision,
        string name,
        CancellationToken cancellationToken)
    {
        string owner;

        try
        {
            owner = await RunAsync(
                    Root,
                    [.. level, "rev-parse", "--absolute-git-dir"],
                    "The repository's own git directory could not be read.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChangeSelectionException)
        {
            return null;
        }

        string store = Path.Combine(
            owner,
            "modules",
            (await RecordedNameAsync(level, revision, name, cancellationToken).ConfigureAwait(false)
                ?? name).Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(store))
        {
            return null;
        }

        try
        {
            await RunAsync(
                    Root,
                    [.. InStore(store, "rev-parse", "--absolute-git-dir")],
                    $"The git directory of '{name}' could not be read.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChangeSelectionException)
        {
            return null;
        }

        return store;
    }

    /// <summary>The name <c>.gitmodules</c> gives a path, when the two have come apart.</summary>
    /// <remarks>
    /// They start equal and stop being so the moment a component is renamed: <c>git mv</c> moves the
    /// gitlink and leaves the store under the old name, which is the same fact that made assembling
    /// a store path from the gitlink wrong in the first place. Asking a present component is exact
    /// and is what happens above; a removed one has nothing left to ask, and its <c>.gitmodules</c>
    /// entry at the revision being read is the only record of the name. Null when the file, the
    /// entry or git itself does not answer, and the caller falls back to the path - which is what
    /// the name is until someone changes one of them.
    /// </remarks>
    private async Task<string?> RecordedNameAsync(
        IReadOnlyList<string> level,
        string revision,
        string path,
        CancellationToken cancellationToken)
    {
        string listing;

        try
        {
            listing = await RunRawAsync(
                    Root,
                    [.. level, "config", "--blob", $"{revision}:.gitmodules",
                        "--get-regexp", @"^submodule\..*\.path$"],
                    string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChangeSelectionException)
        {
            return null;
        }

        const string Key = "submodule.";
        const string Suffix = ".path";

        foreach (string line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(' ', StringComparison.Ordinal);

            if (separator < 0 ||
                !string.Equals(line[(separator + 1)..].Trim(), path, StringComparison.Ordinal))
            {
                continue;
            }

            string key = line[..separator];

            if (key.StartsWith(Key, StringComparison.Ordinal) &&
                key.EndsWith(Suffix, StringComparison.Ordinal))
            {
                return key[Key.Length..^Suffix.Length];
            }
        }

        return null;
    }

    /// <summary>A git command against a component's store, whatever its own checkout is doing.</summary>
    /// <remarks>
    /// Measured, and it is what makes a removed component readable at all. A submodule's store
    /// carries <c>core.worktree</c> pointing back at its checkout, and git chases that before it
    /// does anything else: run inside the store of a component the change deleted, every command -
    /// <c>rev-parse</c> and <c>worktree add</c> alike - dies with
    /// <c>fatal: cannot chdir to '../../../../&lt;path&gt;'</c>. Naming a work tree on the command
    /// line overrides it. Any existing directory serves, since <c>worktree add</c> writes to the
    /// destination it is given; the repository's own root is one that certainly exists. Checked
    /// against a component that is still present too, so this is the single form both take.
    /// </remarks>
    private IReadOnlyList<string> InStore(string store, params string[] arguments) =>
        ["--git-dir", store, "--work-tree", Root, .. arguments];

    /// <summary>Lays out every component a tree records, and every component inside those.</summary>
    /// <remarks>
    /// Recursive, and review found why it has to be. A gitlink recorded inside a submodule is
    /// invisible to the parent's listing, so enumerating once from the parent found the first level
    /// and stopped: the levels below were neither laid out nor reported, and the snapshot came back
    /// claiming to be complete while a whole subtree of it was an empty directory - the one thing
    /// this design exists to prevent, since the base graph would read that emptiness as an answer.
    /// <para>
    /// Nothing is caught here, and review found what the catch that used to be here was hiding. It
    /// turned three different things into the same silent "not present locally": a checkout that
    /// outran the budget, a failure to run git at all, and git's own diagnostic on a non-zero exit.
    /// Only the first is about absent objects, it is already answered by
    /// <see cref="ModuleDirectoryOfAsync"/>, and the others are exactly the failures a user can act
    /// on - once they are told, which they were not.
    /// </para>
    /// </remarks>
    private async Task LayOutComponentsAsync(
        IReadOnlyList<string> level,
        string revision,
        string prefix,
        string destination,
        List<Submodule> laidOut,
        List<string> missing,
        CancellationToken cancellationToken)
    {
        foreach (string path in await ListSubmodulePathsAsync(level, revision, cancellationToken)
                     .ConfigureAwait(false))
        {
            // Named from the root throughout: that is the name the core knows a component by, and
            // the one a listing inside a submodule does not give.
            string full = prefix + path;

            if (await ModuleDirectoryOfAsync(full, path, level, revision, cancellationToken)
                    .ConfigureAwait(false) is not { } moduleDirectory)
            {
                missing.Add(full);

                continue;
            }

            // Recorded before the add is issued rather than after it returns. git registers a
            // worktree as it starts, so one killed half way has already left a registration behind -
            // and review found the record made on success only, which left exactly that case with
            // nothing to remove it.
            laidOut.Add(new Submodule(full, moduleDirectory));

            string commit = await RunAsync(
                    Root,
                    [.. level, "rev-parse", $"{revision}:{path}"],
                    $"The commit recorded for '{full}' could not be read.",
                    cancellationToken)
                .ConfigureAwait(false);

            await RunRawAsync(
                    Root,
                    [.. InStore(
                        moduleDirectory,
                        "worktree", "add", "--quiet", "--detach",
                        Path.Combine(destination, full), commit)],
                    $"The code of '{full}' could not be laid out for reading.",
                    cancellationToken)
                .ConfigureAwait(false);

            await LayOutComponentsAsync(
                    InStore(moduleDirectory),
                    commit,
                    $"{full}/",
                    destination,
                    laidOut,
                    missing,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Two paths naming the same place, compared the way the filesystem would.</summary>
    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>A submodule laid out beneath the snapshot, and the store it was laid out from.</summary>
    private sealed record Submodule(string Path, string ModuleDirectory);

    /// <summary>A worktree, and every worktree opened beneath it, removed together.</summary>
    private sealed class Worktree(
        GitRepository repository,
        string root,
        IReadOnlyList<Submodule> submodules,
        IReadOnlyList<string> missing) : ICodeSnapshot
    {
        public string Root { get; } = root;

        public IReadOnlyList<string> Missing { get; } = missing;

        /// <remarks>
        /// Submodules first: each is a worktree of a different repository, and removing the parent
        /// would leave their registrations behind, pointing at a directory that no longer exists.
        /// Failures are swallowed on purpose - this runs while the run is ending, possibly because
        /// of an error, and one worktree that will not go is not worth failing over. What it is no
        /// longer left to is `git worktree prune`: measured, prune does not clear a registration git
        /// marked `locked`, which is exactly what it leaves behind when an add is killed part way.
        /// </remarks>
        public void Dispose()
        {
            foreach (Submodule submodule in submodules)
            {
                // Through the store, for the reason `InStore` carries: the checkout a component's
                // own config points at may be exactly what the change removed, and git chases it
                // before it reads the command.
                Run(
                    repository.Root,
                    [.. repository.InStore(
                        submodule.ModuleDirectory,
                        "worktree", "remove", "--force", "--force",
                        Path.Combine(Root, submodule.Path))]);
            }

            Run(repository.Root, ["worktree", "remove", "--force", "--force", Root]);
            Scratch.DeleteDirectory(Root);
        }

        /// <remarks>
        /// Twice forced, because once is not enough for the case this exists to clean up. Measured:
        /// `git worktree remove --force` on a registration git left `locked` refuses outright and
        /// says so - "use 'remove -f -f' to override or unlock first".
        /// </remarks>
        private static void Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            try
            {
                GitRepository
                    .RunRawAsync(workingDirectory, arguments, "", default)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is ChangeSelectionException or IOException)
            {
                // One left behind is not worth failing a run that is already ending.
            }
        }
    }


    /// <summary>Reads git's <c>--raw -z</c> output: a record, then a path, then repeat.</summary>
    /// <remarks>
    /// <c>--raw</c> rather than <c>--name-status</c> for one field: the modes.
    /// <code>
    /// :160000 160000 b9ab3c1 0000000 M\0libs/Core\0
    /// :100644 100644 13e7564 0000000 M\0o.txt\0
    /// </code>
    /// Mode <c>160000</c> is a gitlink, which is git's way of saying "a whole component lives here
    /// and I am tracking it as one thing". Saying so is the source's job: the core used to infer it
    /// from whether the path was a directory on disk, which cannot be right for a component the
    /// change removed - measured, a removal reads <c>:160000 000000 … D</c>, and the old mode is
    /// what still identifies it.
    /// </remarks>
    private IEnumerable<FileChange> ReadRaw(string output)
    {
        string[] fields = [.. Split(output)];

        for (int index = 0; index + 1 < fields.Length; index += 2)
        {
            string[] parts = fields[index].TrimStart(':').Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
            {
                continue;
            }

            yield return new FileChange(
                Absolute(fields[index + 1]),
                KindOf(parts[^1]),
                IsWholeComponent: parts[0] == Gitlink || parts[1] == Gitlink);
        }
    }

    /// <summary>The mode git records for a gitlink: a component tracked as one entry.</summary>
    private const string Gitlink = "160000";

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
