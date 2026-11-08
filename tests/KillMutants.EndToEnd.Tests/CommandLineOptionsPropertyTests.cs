using System.Globalization;
using CsCheck;
using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Every option may be given more than once, and the rule for that is written in the parser at the
/// two places a review caught it broken: the last one wins, and a later value clears an earlier
/// <c>none</c>. Review found <c>--break-at none --break-at 80</c> resolving to no gate at all; the
/// same mistake on another option would be found the same way, by someone, one day. Here a model
/// that is too simple to be wrong reads random command lines alongside the parser.
/// </summary>
public class CommandLineOptionsPropertyTests
{
    /// <summary>What the command line should have asked for, as the documented rules say.</summary>
    private sealed class Expected
    {
        public string? Configuration { get; set; }
        public string? TargetFramework { get; set; }
        public int? WorkerCount { get; set; }
        public bool? MeasureCoverage { get; set; }
        public List<string> Exclude { get; } = [];
        public List<string>? IgnoreUntracked { get; set; }
        public List<string>? Mutators { get; set; }
        public List<string>? WithMutators { get; set; }
        public List<string>? WithoutMutators { get; set; }
        public int? VerifyKills { get; set; }
        public string? Since { get; set; }
        public double? Threshold { get; set; }
        public bool ThresholdCleared { get; set; }
        public TimeSpan? StopAfter { get; set; }
        public bool StopAfterCleared { get; set; }
        public string? JsonReportPath { get; set; }
        public string? Resume { get; set; }
        public ProgressMode? Progress { get; set; }
        public TimeSpan? Slow { get; set; }
        public List<string>? IgnoreCalls { get; set; }
    }

    /// <summary>One option as written, and what it does to the model.</summary>
    private sealed record Occurrence(string[] Arguments, Action<Expected> Apply)
    {
        public override string ToString() => string.Join(' ', Arguments);
    }

    private static readonly Gen<string> Word = Gen.String[Gen.Char['a', 'z'], 1, 6];

    private static readonly Gen<string> None = Gen.OneOf("none", "None", "NONE");

    private static Gen<Occurrence> Scalar(string[] names, Gen<string> values, Action<Expected, string> apply) =>
        Gen.Select(Gen.OneOf(names), values, (name, value) =>
            new Occurrence([name, value], expected => apply(expected, value)));

    /// <summary>
    /// A family option: a comma list added to what came before, or <c>none</c>, which empties it.
    /// </summary>
    private static Gen<Occurrence> Families(
        string[] names, Func<Expected, List<string>?> get, Action<Expected, List<string>> set) =>
        Gen.Select(
            Gen.OneOf(names),
            Gen.Frequency((3, Word.Array[1, 3].Select(words => string.Join(',', words))), (1, None)),
            (name, value) => new Occurrence([name, value], expected =>
            {
                if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                {
                    set(expected, []);
                }
                else
                {
                    set(expected, [.. get(expected) ?? [], .. value.Split(',')]);
                }
            }));

    /// <summary>A value, or <c>none</c> to clear it: the last of either wins.</summary>
    private static Gen<Occurrence> Clearable(
        string name, Gen<string> values, Action<Expected, string> apply, Action<Expected> clear) =>
        Gen.Frequency(
            (3, values.Select(value => new Occurrence([name, value], expected => apply(expected, value)))),
            (1, None.Select(value => new Occurrence([name, value], clear))));

    private static readonly Gen<int> Minutes = Gen.Int[1, 600];

    private static TimeSpan FromMinutes(string text) =>
        TimeSpan.FromMinutes(int.Parse(text[..^1], CultureInfo.InvariantCulture));

    private static readonly Gen<Occurrence> AnyOption = Gen.OneOf(
        Scalar(["-c", "--configuration"], Gen.OneOf("Debug", "Release", "Custom"), (e, v) => e.Configuration = v),
        Scalar(["--target-framework"], Gen.OneOf("net10.0", " net11.0 "), (e, v) => e.TargetFramework = v.Trim()),
        Scalar(["-p", "--parallel"], Gen.Int[1, 64].Select(n => n.ToString(CultureInfo.InvariantCulture)),
            (e, v) => e.WorkerCount = int.Parse(v, CultureInfo.InvariantCulture)),
        Gen.Const(new Occurrence(["--no-coverage"], e => e.MeasureCoverage = false)),
        Scalar(["-e", "--exclude"], Word, (e, v) => e.Exclude.Add(v)),
        Scalar(["--ignore-untracked"], Word.Select(w => w + ".received.txt"),
            (e, v) => (e.IgnoreUntracked ??= []).Add(v)),
        Families(["-m", "--mutators"], e => e.Mutators, (e, v) => e.Mutators = v),
        Families(["--with"], e => e.WithMutators, (e, v) => e.WithMutators = v),
        Families(["--without"], e => e.WithoutMutators, (e, v) => e.WithoutMutators = v),
        Scalar(["--verify-kills"], Gen.Int[0, 20].Select(n => n.ToString(CultureInfo.InvariantCulture)),
            (e, v) => e.VerifyKills = int.Parse(v, CultureInfo.InvariantCulture)),
        Scalar(["--since"], Word, (e, v) => e.Since = v),
        Clearable(
            "--break-at",
            Gen.Int[0, 10_000].Select(h => (h / 100d).ToString(CultureInfo.InvariantCulture)),
            (e, v) => (e.Threshold, e.ThresholdCleared) = (double.Parse(v, CultureInfo.InvariantCulture), false),
            e => (e.Threshold, e.ThresholdCleared) = (null, true)),
        Clearable(
            "--stop-after",
            Minutes.Select(m => m.ToString(CultureInfo.InvariantCulture) + "m"),
            (e, v) => (e.StopAfter, e.StopAfterCleared) = (FromMinutes(v), false),
            e => (e.StopAfter, e.StopAfterCleared) = (null, true)),
        Scalar(["--report-json"], Word.Select(w => w + ".json"), (e, v) => e.JsonReportPath = Path.GetFullPath(v)),
        Scalar(["--resume"], Word, (e, v) => e.Resume = v),
        Scalar(["--progress"], Gen.OneOf("log", "Live", "none"),
            (e, v) => e.Progress = CommandLineOptions.ProgressModeOf(v)),
        Scalar(["--slow"], Minutes.Select(m => m.ToString(CultureInfo.InvariantCulture) + "m"),
            (e, v) => e.Slow = FromMinutes(v)),
        Clearable(
            "--ignore-calls",
            Word.Select(w => "*." + w),
            (e, v) => (e.IgnoreCalls ??= []).Add(v),
            e => e.IgnoreCalls = []));

    [Fact]
    public void Repeated_options_resolve_as_the_last_one_given_says()
    {
        AnyOption.Array[0, 8].Sample(
            occurrences =>
            {
                var expected = new Expected();

                foreach (Occurrence occurrence in occurrences)
                {
                    occurrence.Apply(expected);
                }

                CommandLineOptions parsed = CommandLineOptions.Parse(
                    ["/work", .. occurrences.SelectMany(occurrence => occurrence.Arguments)]);

                Assert.Equal(expected.Configuration, parsed.Configuration);
                Assert.Equal(expected.TargetFramework, parsed.TargetFramework);
                Assert.Equal(expected.WorkerCount, parsed.WorkerCount);
                Assert.Equal(expected.MeasureCoverage, parsed.MeasureCoverage);
                Assert.Equal(expected.Exclude, parsed.Exclude);
                Assert.Equal(expected.IgnoreUntracked, parsed.IgnoreUntracked);
                Assert.Equal(expected.Mutators, Names(parsed.Mutators));
                Assert.Equal(expected.WithMutators, Names(parsed.WithMutators));
                Assert.Equal(expected.WithoutMutators, Names(parsed.WithoutMutators));
                Assert.Equal(expected.VerifyKills, parsed.VerifyKills);
                Assert.Equal(expected.Since, parsed.Since);
                Assert.Equal(expected.Threshold, parsed.Threshold);
                Assert.Equal(expected.ThresholdCleared, parsed.ThresholdCleared);
                Assert.Equal(expected.StopAfter, parsed.StopAfter);
                Assert.Equal(expected.StopAfterCleared, parsed.StopAfterCleared);
                Assert.Equal(expected.JsonReportPath, parsed.JsonReportPath);
                Assert.Equal(expected.Resume, parsed.Resume);
                Assert.Equal(expected.Progress, parsed.Progress);
                Assert.Equal(expected.Slow, parsed.Slow);
                Assert.Equal(expected.IgnoreCalls, parsed.IgnoreCalls);
            },
            iter: 5_000,
            print: occurrences => string.Join(' ', occurrences.Select(occurrence => occurrence.ToString())));
    }

    private static List<string>? Names(IReadOnlyList<KillMutants.Mutations.MutatorName>? names) =>
        names is null ? null : [.. names.Select(name => name.ToString())];
}
