using System.IO.Enumeration;

namespace KillMutants.Filtering;

/// <summary>
/// The untracked files a series does not count when it asks whether its tree is exactly its commit:
/// the files a project's own tests write into the source tree, named by the project. See DEC0025.
/// </summary>
/// <remarks>
/// <para>
/// Written and matched as <c>--exclude</c> is - against the path relative to the directory the run
/// was pointed at, with <c>/</c>, without regard to case, <c>*</c> covering <c>/</c> too - so that a
/// project has one dialect of patterns and not two.
/// </para>
/// <para>
/// A pattern is refused when it could cover a file that changes what is built. An untracked
/// <c>.cs</c> under a project is compiled, so a series that ignored one would inherit verdicts reached
/// on other code - which is the one thing DEC0015's rule exists to prevent. The test is exact for the
/// patterns this dialect can write: two patterns made of literals, <c>?</c> and <c>*</c> are asked
/// whether any path matches both, rather than tried against a few sample names, which a pattern
/// such as <c>a*.cs</c> would slip past. Case is ignored in that test whatever the platform, since
/// Windows compiles <c>X.CS</c>. It stops the obvious mistakes, not a pattern written to get around
/// it; the compiler's inputs are checked again once the build has named them.
/// </para>
/// </remarks>
public static class UntrackedPatterns
{
    /// <summary>
    /// The files that change what is built, retained when DEC0025 was decided; written in the same
    /// dialect, so a name that must sit at any depth is listed both at the top and below it.
    /// </summary>
    /// <remarks>
    /// Growing this list after a release can refuse a pattern that used to pass, which is why the
    /// record asks for it to be reviewed before the first one.
    /// </remarks>
    public static IReadOnlyList<string> Sensitive { get; } =
    [
        "*.cs",
        "*.csproj",
        "*.props",
        "*.targets",
        "*.globalconfig",
        "*.resx",
        ".editorconfig",
        "*/.editorconfig",
        "global.json",
        "*/global.json",
        "NuGet.config",
        "*/NuGet.config",
    ];

    /// <summary>
    /// Refuses a pattern that is empty, that would ignore every untracked file, or that could cover a
    /// file of <see cref="Sensitive"/>.
    /// </summary>
    /// <exception cref="ArgumentException">One of <paramref name="patterns"/> is refused; the message says why.</exception>
    public static void Validate(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("'--ignore-untracked' needs a pattern, and was given an empty one.");
            }

            Token[] tokens = Tokenise(Normalise(pattern));

            // Said apart from the overlap below, which would name '*.cs' and be right for the wrong
            // reason: what is wrong with '*' is not that it covers source files but that it covers
            // everything.
            if (tokens.Any(token => token.Kind == TokenKind.Star) &&
                tokens.All(token => token.Kind != TokenKind.Literal || token.Character == '/'))
            {
                throw new ArgumentException(
                    $"'--ignore-untracked {pattern}' would ignore every untracked file, which is the check " +
                    "on the tree switched off rather than an exception to it. Name the files the tests " +
                    "write instead, such as '*.received.html'.");
            }

            if (Sensitive.FirstOrDefault(sensitive => Overlap(tokens, Tokenise(sensitive))) is { } covered)
            {
                throw new ArgumentException(
                    $"'--ignore-untracked {pattern}' could match the same files as '{covered}', and such a " +
                    "file changes what is built: a series that ignored it would inherit verdicts reached on " +
                    "other code. Name the files more precisely, by their whole extension - " +
                    "'*.received.html' rather than '*.received.*'.");
            }
        }
    }

    /// <summary>True when <paramref name="relativePath"/> matches one of <paramref name="patterns"/>.</summary>
    /// <param name="patterns">The patterns, already validated.</param>
    /// <param name="relativePath">
    /// A path relative to the directory the run was pointed at, with <c>/</c> or the platform's
    /// separator.
    /// </param>
    public static bool Matches(IEnumerable<string> patterns, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(relativePath);

        string path = Normalise(relativePath);

        return patterns.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(Normalise(pattern), path, ignoreCase: true));
    }

    /// <summary>
    /// True when some path matches both patterns, compared without regard to case.
    /// </summary>
    /// <remarks>
    /// A walk over the pairs of positions the two patterns can be in after reading the same text,
    /// which is finite - one pair per position of each - so the answer is exact and cheap. A star can
    /// be passed over without reading anything; otherwise both sides read one character, which two
    /// literals can share only when they are the same letter.
    /// </remarks>
    internal static bool Overlap(string left, string right) =>
        Overlap(Tokenise(Normalise(left)), Tokenise(Normalise(right)));

    private static bool Overlap(Token[] left, Token[] right)
    {
        var seen = new HashSet<(int, int)>();
        var pending = new Stack<(int Left, int Right)>();

        pending.Push((0, 0));

        while (pending.Count > 0)
        {
            (int i, int j) = pending.Pop();

            if (!seen.Add((i, j)))
            {
                continue;
            }

            if (i == left.Length && j == right.Length)
            {
                return true;
            }

            if (i < left.Length && left[i].Kind == TokenKind.Star)
            {
                pending.Push((i + 1, j));
            }

            if (j < right.Length && right[j].Kind == TokenKind.Star)
            {
                pending.Push((i, j + 1));
            }

            if (i < left.Length && j < right.Length && CanShare(left[i], right[j]))
            {
                pending.Push((
                    left[i].Kind == TokenKind.Star ? i : i + 1,
                    right[j].Kind == TokenKind.Star ? j : j + 1));
            }
        }

        return false;
    }

    private static bool CanShare(Token left, Token right) =>
        left.Kind != TokenKind.Literal ||
        right.Kind != TokenKind.Literal ||
        char.ToUpperInvariant(left.Character) == char.ToUpperInvariant(right.Character);

    /// <summary>
    /// Reads a pattern the way <see cref="FileSystemName.MatchesSimpleExpression"/> does: <c>*</c>,
    /// <c>?</c>, and a backslash that makes the next character literal.
    /// </summary>
    private static Token[] Tokenise(string pattern)
    {
        List<Token> tokens = [];

        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];

            if (character == '\\' && index + 1 < pattern.Length)
            {
                tokens.Add(new Token(TokenKind.Literal, pattern[++index]));
            }
            else if (character == '*')
            {
                // Two stars in a row read exactly as one, and keeping one keeps the walk small.
                if (tokens.Count == 0 || tokens[^1].Kind != TokenKind.Star)
                {
                    tokens.Add(new Token(TokenKind.Star, character));
                }
            }
            else
            {
                tokens.Add(new Token(character == '?' ? TokenKind.Any : TokenKind.Literal, character));
            }
        }

        return [.. tokens];
    }

    /// <summary>
    /// Writes the separators as <c>/</c>. Only the platform's alternative separator is turned, never
    /// a backslash on Linux and macOS, where it is the escape the pattern was written with.
    /// </summary>
    private static string Normalise(string path) =>
        Path.DirectorySeparatorChar == '\\' ? path.Replace('\\', '/') : path;

    private enum TokenKind
    {
        Literal,
        Any,
        Star,
    }

    private readonly record struct Token(TokenKind Kind, char Character);
}
