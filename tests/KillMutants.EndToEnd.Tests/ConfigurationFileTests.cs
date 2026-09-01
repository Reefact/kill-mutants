using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The file is written by hand and read by a tool, so it is forgiving about how people write and
/// unforgiving about what they mean.
/// </summary>
public class ConfigurationFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_project_that_keeps_no_file_has_no_settings()
    {
        Assert.Null(ConfigurationFile.LoadFrom(_directory));
    }

    [Fact]
    public void Every_setting_is_read()
    {
        ConfigurationFile? file = Write(
            """
            {
              "configuration": "Debug",
              "exclude": ["tests/fixtures/*"],
              "mutators": ["Comparison"],
              "without": ["StringLiteral"],
              "parallel": 3,
              "coverage": false,
              "breakAt": 70,
              "reportJson": "artifacts/mutation.json"
            }
            """);

        Assert.NotNull(file);
        Assert.Equal("Debug", file.Configuration);
        Assert.Equal(["tests/fixtures/*"], file.Exclude);
        Assert.Equal(["Comparison"], file.Mutators);
        Assert.Equal(["StringLiteral"], file.Without);
        Assert.Equal(3, file.Parallel);
        Assert.False(file.Coverage);
        Assert.Equal(70, file.BreakAt);
        Assert.Equal("artifacts/mutation.json", file.ReportJson);
        Assert.Equal(_directory, file.Directory);
    }

    [Fact]
    public void An_empty_file_is_a_file_that_says_nothing()
    {
        ConfigurationFile? file = Write("{}");

        Assert.NotNull(file);
        Assert.Null(file.Configuration);
        Assert.Null(file.Exclude);
    }

    /// <summary>Written by hand, so read the way a person writes.</summary>
    [Fact]
    public void Comments_and_a_trailing_comma_are_accepted()
    {
        ConfigurationFile? file = Write(
            """
            {
              // Error messages here are not asserted on, so this family only adds noise.
              "without": ["StringLiteral"],
            }
            """);

        Assert.Equal(["StringLiteral"], file!.Without);
    }

    /// <summary>
    /// The rule that matters. A misspelt key silently ignored would run with settings nobody asked
    /// for - the same failure the command line refuses a misspelt mutator family for.
    /// </summary>
    [Fact]
    public void A_misspelt_key_is_refused_and_named()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Write("""{ "configurations": "Debug" }"""));

        Assert.Contains("configurations", error.Message, StringComparison.Ordinal);
        Assert.Contains(ConfigurationFile.Name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_of_the_wrong_shape_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Write("""{ "parallel": "four" }"""));
    }

    [Fact]
    public void A_file_that_is_not_json_at_all_is_refused_by_name()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Write("parallel = 3"));

        Assert.Contains(ConfigurationFile.Name, error.Message, StringComparison.Ordinal);
    }

    private ConfigurationFile? Write(string json)
    {
        File.WriteAllText(Path.Combine(_directory, ConfigurationFile.Name), json);

        return ConfigurationFile.LoadFrom(_directory);
    }
}
