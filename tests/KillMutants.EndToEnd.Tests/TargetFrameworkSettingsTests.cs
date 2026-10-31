using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// <c>--target-framework</c> and its <c>targetFramework</c> key, which follow the one rule every
/// habit follows: the command line wins, otherwise the file, otherwise nothing. See DEC0021.
/// </summary>
public class TargetFrameworkSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void Nothing_is_chosen_unless_somebody_chose()
    {
        Assert.Null(RunSettings.From(Asked(), file: null).TargetFramework);
    }

    [Fact]
    public void The_command_line_names_the_framework()
    {
        Assert.Equal("net10.0", RunSettings.From(Asked("--target-framework", "net10.0"), file: null).TargetFramework);
    }

    [Fact]
    public void The_file_names_it_when_the_command_line_does_not()
    {
        var file = new ConfigurationFile(TargetFramework: " net8.0 ");

        Assert.Equal("net8.0", RunSettings.From(Asked(), file).TargetFramework);
    }

    [Fact]
    public void The_command_line_wins_over_the_file()
    {
        var file = new ConfigurationFile(TargetFramework: "net8.0");

        Assert.Equal(
            "net10.0",
            RunSettings.From(Asked("--target-framework", "net10.0"), file).TargetFramework);
    }

    [Fact]
    public void A_blank_value_in_the_file_names_nothing()
    {
        Assert.Null(RunSettings.From(Asked(), new ConfigurationFile(TargetFramework: "  ")).TargetFramework);
    }

    [Theory]
    [InlineData("--target-framework")]
    public void The_option_without_a_value_is_refused(string option)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => Asked(option));

        Assert.Contains(option, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_option_followed_by_an_empty_value_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Asked("--target-framework", " "));
    }
}
