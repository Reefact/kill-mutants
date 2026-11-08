using CsCheck;
using KillMutants.Mutations;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// A run can be killed at any byte of any line it is writing, and the run that resumes the series
/// then reads that log and appends to it (DEC0014, DEC0015). Each of those has its own example test;
/// this is the promise they add up to, checked at every place a kill can land rather than at the
/// two or three a person thinks of.
/// </summary>
public sealed class VerdictLogPropertyTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Cut anywhere, the log reads back as the verdicts whose line was finished - line feed and all
    /// - and the run that appends to it adds its own after exactly those, so that what it read is
    /// what the file holds. <c>PlanKeeper</c> counts where each run's lines begin on that
    /// agreement, and a later resume inherits from it.
    /// </summary>
    [Fact]
    public void A_log_cut_at_any_byte_reads_as_its_finished_lines_and_is_continued_after_them()
    {
        Gen<(MutantResult[] Written, Cut Cut, MutantResult[] Appended)> cases = Gen.Select(
            LoggedVerdictPropertyTests.Results.Array[1, 5],
            Gen.Select(
                Gen.Enum<CutKind>(), Gen.Double[0, 1], Gen.Int[0, 4],
                (kind, fraction, line) => new Cut(kind, fraction, line)),
            LoggedVerdictPropertyTests.Results.Array[0, 3]);

        cases.Sample(
            (written, cut, appended) =>
            {
                string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".jsonl");

                using (VerdictLog log = VerdictLog.Create(path))
                {
                    foreach (MutantResult result in written)
                    {
                        log.Report(result);
                    }
                }

                byte[] content = File.ReadAllBytes(path);
                int length = cut.LengthOf(content);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                {
                    stream.SetLength(length);
                }

                // A line is finished when its line feed made it to the file. UTF-8 never puts that
                // byte inside a character, so counting it is counting lines.
                int finished = content.AsSpan(0, length).Count((byte)'\n');
                MutantResult[] kept = written[..finished];

                AssertRead(kept, VerdictLog.Read(path));

                using (VerdictLog log = VerdictLog.Append(path))
                {
                    foreach (MutantResult result in appended)
                    {
                        log.Report(result);
                    }
                }

                AssertRead([.. kept, .. appended], VerdictLog.Read(path));
            },
            iter: 500,
            print: sample =>
                $"{sample.Item2} of: " +
                string.Join(" | ", sample.Item1.Select(LoggedVerdictPropertyTests.Print)) +
                $"; then appended {sample.Item3.Length}");
    }

    private enum CutKind
    {
        /// <summary>Anywhere at all, which is mostly inside a line.</summary>
        Anywhere,

        /// <summary>After a line's last character and before its line feed: complete, but not finished.</summary>
        BeforeALineFeed,

        /// <summary>Just after a line feed, where a kill between two verdicts leaves the file.</summary>
        AfterALineFeed,
    }

    /// <summary>
    /// Where the kill lands. The two boundaries are drawn on purpose: a cut at a uniformly random
    /// byte almost never falls on one, and they are where reading and appending must agree.
    /// </summary>
    private sealed record Cut(CutKind Kind, double Fraction, int Line)
    {
        public int LengthOf(byte[] content)
        {
            int[] lineFeeds = [.. Enumerable.Range(0, content.Length).Where(index => content[index] == '\n')];
            int lineFeed = lineFeeds[Line % lineFeeds.Length];

            return Kind switch
            {
                CutKind.BeforeALineFeed => lineFeed,
                CutKind.AfterALineFeed => lineFeed + 1,
                _ => (int)Math.Round(Fraction * content.Length),
            };
        }
    }

    private static void AssertRead(MutantResult[] expected, IReadOnlyList<LoggedVerdict> read)
    {
        Assert.Equal(expected.Length, read.Count);

        for (int index = 0; index < expected.Length; index++)
        {
            LoggedVerdictPropertyTests.AssertInherits(expected[index], read[index]);
        }
    }
}
