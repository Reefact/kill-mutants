using Slugger;

namespace KillMutants;

/// <summary>Draws the name a run's directory takes, from the kill-mutants theme.</summary>
/// <remarks>
/// <para>
/// The theme is <c>Resources/kill-mutants.json</c>, embedded in this assembly, and this repository
/// is where it is defined: it began in Reefact/slugger, which no longer carries it. The library
/// compiles in only its own three themes, and reading one from the user's home directory would name
/// this tool's directories after whatever happened to be installed there. Changing the vocabulary
/// means editing that file here, and its <c>meta</c> block is where its version is kept.
/// </para>
/// <para>
/// Loaded once, lazily. Validating the theme costs most of a second, and a run draws two names at
/// most. The full rules are kept rather than waived: an embedded theme edited below the library's
/// floors is then refused at the first draw, rather than quietly shrinking the vocabulary every
/// collision check relies on.
/// </para>
/// </remarks>
internal static class ScratchNames
{
    /// <summary>
    /// The width of the <c>killmutants-</c> plus thirty-two-digit GUID name these replace. The theme
    /// allows sixty-three; staying at forty-four means no path that fit under the old name stops
    /// fitting under the new one, which matters on Windows, where a sandbox copies a whole test output
    /// tree beneath it. Measured over two thousand draws, the longest name was thirty-nine.
    /// </summary>
    internal const int MaxLength = 44;

    private const string ResourceName = "KillMutants.Themes.kill-mutants.json";
    private const string ThemeName = "kill-mutants";

    private static readonly Lazy<SlugGenerator> Generator = new(Load);

    /// <summary>One name, from the shared random source: a different one on every call.</summary>
    public static string Draw() => Generator.Value.Next();

    private static SlugGenerator Load()
    {
        using Stream stream = typeof(ScratchNames).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The theme '{ResourceName}' is not embedded in {typeof(ScratchNames).Assembly.GetName().Name}.");

        SluggerTheme theme = SluggerTheme.LoadFromStreamOrThrow(stream, ThemeName);

        // The theme's own style - an adjective and a participle before the noun, one word each -
        // and then the ceiling, which the theme does not know about. ForOrThrow checks the floors
        // again under that ceiling, so a limit that starves the theme is refused here, not drawn.
        return SlugGenerator.ForOrThrow(theme, style => style with { MaxLength = MaxLength });
    }
}
