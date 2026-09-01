using KillMutants.Coverage;
using KillMutants.Mutations;

namespace KillMutants.Core.Tests.Coverage;

/// <summary>
/// Three answers, and the difference between two of them is a whole class of silent false verdicts:
/// "this test reached nothing" and "we could not find out" must never be the same value.
/// </summary>
public class CoverageFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"killmutants-test-{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The recorder writes its file the moment it is first touched, so no file means the test never
    /// entered the assembly under test. That is a measurement, and an empty one.
    /// </summary>
    [Fact]
    public void No_file_at_all_means_the_test_reached_nothing()
    {
        Assert.Empty(CoverageFile.Read(_path)!);
    }

    [Fact]
    public void A_complete_file_yields_the_sites_it_names()
    {
        Write("3", "7", CoverageProbe.CompletionMarker);

        Assert.Equal(
            [MutantId.FromValue(3), MutantId.FromValue(7)],
            CoverageFile.Read(_path));
    }

    [Fact]
    public void A_complete_file_with_no_hits_is_an_answer_rather_than_a_failure()
    {
        Write(CoverageProbe.CompletionMarker);

        Assert.Empty(CoverageFile.Read(_path)!);
    }

    /// <summary>
    /// The regression test. A process killed part-way through leaves the hits it had recorded and no
    /// marker; reading those as the complete answer would mark every site it had not reached yet as
    /// uncovered, and those mutants would never be run.
    /// </summary>
    [Fact]
    public void A_file_without_the_completion_marker_is_not_an_answer()
    {
        Write("3", "7");

        Assert.Null(CoverageFile.Read(_path));
    }

    [Fact]
    public void An_empty_file_is_not_an_answer()
    {
        Write();

        Assert.Null(CoverageFile.Read(_path));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("3.5")]
    [InlineData("")]
    public void A_file_containing_anything_the_recorder_would_not_write_is_not_an_answer(string line)
    {
        Write("3", line, CoverageProbe.CompletionMarker);

        Assert.Null(CoverageFile.Read(_path));
    }

    private void Write(params string[] lines) =>
        File.WriteAllText(_path, string.Concat(lines.Select(line => line + "\n")));
}
