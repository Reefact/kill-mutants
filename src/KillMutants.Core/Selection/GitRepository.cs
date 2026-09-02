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

    /// <summary>
    /// Writes the whole of <paramref name="revision"/> into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>git archive</c> and not <c>git worktree add</c>: a worktree is registered inside the
    /// user's repository and has to be removed again afterwards, so an interrupted run leaves state
    /// behind in a directory that is not ours. An archive is a stream, and what it produces is an
    /// ordinary directory that can be deleted like any other temporary file.
    /// </para>
    /// <para>
    /// Extracted with <see cref="TarFile"/> rather than by piping into <c>tar</c>, which keeps the
    /// dependency at git alone.
    /// </para>
    /// </remarks>
    public async Task ExportAsync(
        string revision,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        string archive = Path.Combine(Path.GetTempPath(), $"killmutants-{Guid.NewGuid():N}.tar");

        try
        {
            await RunRawAsync(
                    Root,
                    ["archive", "--format=tar", "-o", archive, revision],
                    $"The tree at '{revision}' could not be exported.",
                    cancellationToken)
                .ConfigureAwait(false);

            Directory.CreateDirectory(destination);

            try
            {
                TarFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ChangeSelectionException(
                    $"The tree at '{revision}' could not be written to '{destination}': " +
                    exception.Message,
                    exception);
            }
        }
        finally
        {
            Scratch.DeleteFile(archive);
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
