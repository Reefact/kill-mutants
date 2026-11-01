using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// DEC0026: the calls a project does not hear about come from <c>--ignore-calls</c> or the
/// <c>ignoreCalls</c> key, the command line replacing the file's list, and a pattern nobody can have
/// meant is refused from either before anything runs.
/// </summary>
public class IgnoreCallsSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void Nothing_is_ignored_unless_asked()
    {
        Assert.Empty(RunSettings.From(Asked(), file: null).IgnoreCalls!);
    }

    [Fact]
    public void The_patterns_reach_the_settings_in_the_order_written()
    {
        RunSettings settings = RunSettings.From(
            Asked("--ignore-calls", "*.ConfigureAwait", "--ignore-calls", "System.ArgumentException.ThrowIf*"),
            file: null);

        Assert.Equal(["*.ConfigureAwait", "System.ArgumentException.ThrowIf*"], settings.IgnoreCalls);
    }

    [Fact]
    public void The_file_keeps_the_list_and_the_command_line_replaces_it()
    {
        var file = new ConfigurationFile(IgnoreCalls: ["*.ThrowIfNull"]) { Directory = "/work" };

        Assert.Equal(["*.ThrowIfNull"], RunSettings.From(Asked(), file).IgnoreCalls);
        Assert.Equal(["*.Append"], RunSettings.From(Asked("--ignore-calls", "*.Append"), file).IgnoreCalls);
        Assert.Empty(RunSettings.From(Asked("--ignore-calls", "none"), file).IgnoreCalls!);
    }

    [Fact]
    public void A_pattern_after_none_fills_the_list_again()
    {
        RunSettings settings = RunSettings.From(
            Asked("--ignore-calls", "none", "--ignore-calls", "*.Append"), file: null);

        Assert.Equal(["*.Append"], settings.IgnoreCalls);
    }

    [Theory]
    [InlineData("--ignore-calls")]
    [InlineData("--ignore-calls", "")]
    public void A_pattern_is_needed(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => Asked(arguments));
    }

    [Fact]
    public void A_pattern_that_would_silence_every_call_is_refused_from_either_source()
    {
        ArgumentException fromCommandLine = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--ignore-calls", "*"), file: null));
        ArgumentException fromFile = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(IgnoreCalls: ["*.*"]) { Directory = "/work" }));

        Assert.Contains("every call", fromCommandLine.Message, StringComparison.Ordinal);
        Assert.Contains("'ignoreCalls' in", fromFile.Message, StringComparison.Ordinal);
    }
}
