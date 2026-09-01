namespace KillMutants;

/// <summary>Removes the temporary files and directories a run leaves behind.</summary>
/// <remarks>
/// One policy, stated once: a run must never fail because it could not tidy up after itself. The
/// sandboxes, the coverage recorder's output and the runner's result files all end this way, and
/// four copies of the same three-line <c>try</c> block invited one of them to drift into throwing.
/// </remarks>
internal static class Scratch
{
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
