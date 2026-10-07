namespace KillMutants;

/// <summary>
/// Creates the directories a run works in, and removes the temporary files and directories a run
/// leaves behind.
/// </summary>
/// <remarks>
/// One policy for the removals, stated once: a run must never fail because it could not tidy up
/// after itself. The sandboxes, the coverage recorder's output and the runner's result files all end
/// this way, and four copies of the same three-line <c>try</c> block invited one of them to drift
/// into throwing.
/// </remarks>
public static class Scratch
{
    /// <summary>How many names are tried before a parent is declared full.</summary>
    internal const int Attempts = 100;

    /// <summary>
    /// Creates a directory of the run's own under the temporary path, named from the kill-mutants
    /// theme: <c>feral-hissing-wireborn</c> rather than thirty-two hexadecimal digits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nobody reads these names while a run lives; the tool never prints them. They are read after a
    /// run was killed, which is the one way a run leaves a directory behind (RB-006), by whoever
    /// lists the temporary directory to find out what is sitting there. A phrase in this tool's own
    /// vocabulary says whose it is. The <c>killmutants-</c> prefix said the same thing, and was
    /// dropped for saying it twice.
    /// </para>
    /// <para>
    /// Uniqueness is checked, not drawn. A random suffix makes two runs collide only by chance;
    /// testing the name against the file system makes them collide only if they draw the same words
    /// in the same instant. The check is not atomic - <see cref="Directory.CreateDirectory(string)"/>
    /// does not refuse a directory that already exists - so what it buys is narrowing a one-in-
    /// several-hundred-thousand draw to that draw landing inside the same microseconds. Accepted.
    /// Anything already at the path counts as taken, a file included: creating a directory over a
    /// file throws, and a name that throws is not a name to keep.
    /// </para>
    /// <para>
    /// The search is bounded. A parent where every name is taken - a theme reduced to a handful of
    /// words, a temporary directory somebody filled with them - would otherwise be searched forever,
    /// with nothing to say why. A hundred draws cost under two seconds, and then the refusal names
    /// the parent.
    /// </para>
    /// </remarks>
    /// <returns>The directory's full path. It exists, and nothing else owns it.</returns>
    /// <exception cref="IOException">No free name was found in <see cref="Attempts"/> draws.</exception>
    public static string CreateDirectory() => CreateDirectory(Path.GetTempPath(), ScratchNames.Draw);

    /// <summary>The search, with the parent and the draw handed in so a test can script both.</summary>
    internal static string CreateDirectory(string parent, Func<string> draw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentNullException.ThrowIfNull(draw);

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            string candidate = Path.Combine(parent, draw());

            if (Path.Exists(candidate))
            {
                continue;
            }

            Directory.CreateDirectory(candidate);

            return candidate;
        }

        throw new IOException(
            $"No free name was found under '{parent}' in {Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)} draws.");
    }

    /// <summary>Deletes a file, ignoring the fact that it could not be deleted.</summary>
    public static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file is not worth failing a run over.
        }
        catch (UnauthorizedAccessException)
        {
            // Nor is one another process still holds open.
        }
    }

    /// <summary>Deletes a directory and its contents, ignoring the same failures.</summary>
    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
