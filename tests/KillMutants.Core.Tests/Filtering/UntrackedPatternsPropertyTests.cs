using System.Text;
using CsCheck;
using KillMutants.Filtering;

namespace KillMutants.Core.Tests.Filtering;

/// <summary>
/// <see cref="UntrackedPatterns.Overlap(string, string)"/> claims to be exact rather than tried
/// against a few sample names, and that claim is what DEC0025's refusal stands on: an overlap it
/// misses is an untracked <c>.cs</c> a series ignores, and verdicts inherited across code that
/// changed. A claim about every pattern is checked against many, with the matcher the run itself
/// uses - <see cref="UntrackedPatterns.Matches"/> - as the oracle, so the two can never drift apart
/// unnoticed.
/// </summary>
public class UntrackedPatternsPropertyTests
{
    /// <summary>
    /// Letters whose case the two sides could fold differently, beside plain ones: the Kelvin sign,
    /// a dotless i, a titlecase digraph and a letter with an accent. Windows compiles <c>X.CS</c>,
    /// so case is where an overlap is easiest to miss.
    /// </summary>
    private const string PathCharacters = "abcsABCS./" + "éÉǅǆKkıI";

    /// <summary>
    /// A pattern meant to match <paramref name="path"/>: each character kept, its case raised,
    /// escaped, read as <c>?</c> or swallowed by a <c>*</c>, and a <c>*</c> that reads nothing
    /// slipped in anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from a path rather than drawn at random, because two random patterns and a random path
    /// almost never all agree, and a property that is mostly vacuous checks almost nothing. The
    /// escape is left out on Windows, where a backslash is a separator and not an escape - see
    /// <c>UntrackedPatterns.Normalise</c>.
    /// </para>
    /// <para>
    /// Meant to, not certain to: the properties keep only the cases the real matcher agrees with.
    /// Measured on .NET 10, <see cref="System.IO.Enumeration.FileSystemName.MatchesSimpleExpression"/>
    /// reads a pattern that is a <c>*</c> followed by a tail with no other wildcard as "ends with
    /// that tail", backslash included: <c>*.c\s</c> matches <c>a.c\s</c> and not <c>.cs</c>, while
    /// <c>a*.c\s</c> matches <c>ab.cs</c>. <see cref="UntrackedPatterns.Overlap(string, string)"/>
    /// reads the escape in both, so on that one shape it refuses more than the matcher would ever
    /// match - the cautious side, and the reason the oracle here is the matcher rather than this
    /// construction.
    /// </para>
    /// </remarks>
    private static Gen<string> PatternMatching(string path) =>
        Gen.Select(Gen.Int[0, 9].Array[path.Length], Gen.Int[0, 4].Array[path.Length + 1], (choices, stars) =>
        {
            var pattern = new StringBuilder();

            for (int index = 0; index <= path.Length; index++)
            {
                if (stars[index] == 0)
                {
                    pattern.Append('*');
                }

                if (index == path.Length)
                {
                    break;
                }

                char character = path[index];

                switch (choices[index])
                {
                    case 0 or 1:
                        pattern.Append(char.ToUpperInvariant(character));
                        break;

                    case 2 when Path.DirectorySeparatorChar == '/':
                        pattern.Append('\\').Append(character);
                        break;

                    case 3 or 4:
                        pattern.Append('?');
                        break;

                    case 5:
                        pattern.Append('*');
                        break;

                    default:
                        pattern.Append(character);
                        break;
                }
            }

            return pattern.ToString();
        });

    /// <summary>Not empty: the matcher matches no pattern against an empty name, which no file has.</summary>
    private static readonly Gen<string> AnyPath = Gen.String[Gen.Char[PathCharacters], 1, 10];

    /// <summary>
    /// The direction that keeps DEC0025 safe: when one path matches both, the overlap is seen.
    /// </summary>
    [Fact]
    public void Two_patterns_that_match_the_same_path_are_seen_to_overlap()
    {
        Gen<(string Path, string Left, string Right)> cases =
            from path in AnyPath
            from left in PatternMatching(path)
            from right in PatternMatching(path)
            where UntrackedPatterns.Matches([left], path) && UntrackedPatterns.Matches([right], path)
            select (path, left, right);

        cases.Sample(
            (path, left, right) =>
            {
                Assert.True(UntrackedPatterns.Overlap(left, right));
                Assert.True(UntrackedPatterns.Overlap(right, left));
            },
            iter: 20_000);
    }

    /// <summary>
    /// The other direction, which is what makes the test exact and not merely cautious: an overlap
    /// is reported only when some path does match both - so a pattern is never refused for a
    /// collision that cannot happen.
    /// </summary>
    /// <remarks>
    /// Checked by looking for that path, which is only possible on small patterns: a shortest
    /// witness has no character both sides read with a <c>*</c> - it could be dropped - so it is no
    /// longer than the two patterns' other tokens together, and every character of it can be one
    /// of the literals or, for a <c>?</c>, any one of them.
    /// </remarks>
    [Fact]
    public void An_overlap_is_reported_exactly_when_some_path_matches_both_patterns()
    {
        const string literals = "ab.";
        Gen<string> smallPattern = Gen.String[Gen.Char[literals + "AB*?"], 1, 4];

        Gen.Select(smallPattern, smallPattern).Sample(
            (left, right) =>
            {
                // At least one character: the matcher matches nothing against an empty name.
                int longest = Math.Max(1, left.Count(c => c != '*') + right.Count(c => c != '*'));
                bool witnessed = Paths(literals, longest).Any(path =>
                    UntrackedPatterns.Matches([left], path) && UntrackedPatterns.Matches([right], path));

                Assert.Equal(witnessed, UntrackedPatterns.Overlap(left, right));
            },
            iter: 2_000);

        static IEnumerable<string> Paths(string alphabet, int longest)
        {
            IEnumerable<string> level = [string.Empty];

            for (int length = 0; length <= longest; length++)
            {
                foreach (string path in level)
                {
                    yield return path;
                }

                level = level.SelectMany(path => alphabet.Select(c => path + c)).ToList();
            }
        }
    }

    /// <summary>
    /// What the refusal is for, said on the public entry point: a pattern that matches a file the
    /// build reads is refused, wherever that file sits and however its name is cased.
    /// </summary>
    [Fact]
    public void A_pattern_that_matches_a_file_the_build_reads_is_refused()
    {
        Gen<string> segment = Gen.String[Gen.Char["abAB."], 1, 3];
        Gen<string> directories = segment.Array[0, 2]
            .Select(names => string.Concat(names.Select(name => name + "/")));
        Gen<string> name = Gen.OneOf(
            Gen.Select(
                Gen.String[Gen.Char["abAB."], 0, 3],
                Gen.OneOf(".cs", ".csproj", ".props", ".targets", ".globalconfig", ".resx"),
                (stem, extension) => stem + extension),
            Gen.OneOf(".editorconfig", "global.json", "NuGet.config"));

        Gen<(string Path, string Pattern)> cases =
            from path in Gen.Select(directories, name, Gen.Bool, (dirs, file, upper) =>
                upper ? (dirs + file).ToUpperInvariant() : dirs + file)
            from pattern in PatternMatching(path)
            where UntrackedPatterns.Matches([pattern], path)
            select (path, pattern);

        cases.Sample(
            (path, pattern) =>
            {
                // The generator's own promise, checked so that a name it got wrong is reported as
                // that and not as a pattern let through.
                Assert.True(
                    UntrackedPatterns.Matches(UntrackedPatterns.Sensitive, path),
                    $"'{path}' was built to be a file the build reads.");

                Assert.Throws<ArgumentException>(() => UntrackedPatterns.Validate([pattern]));
            },
            iter: 20_000);
    }
}
