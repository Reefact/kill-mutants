using System.Reflection;

namespace KillMutants.Projects;

/// <summary>Reads which xUnit a built test project actually runs on.</summary>
/// <remarks>
/// <para>
/// From the assembly in the output directory rather than from the project file, because the project
/// file often does not say. Under Central Package Management the <c>PackageReference</c> item carries
/// no version at all - measured against the .NET 10 SDK, which returns the bare identity - and a
/// floating or transitively pinned version is not written down anywhere either. The assembly that
/// will actually be loaded is the only answer that cannot be wrong.
/// </para>
/// <para>
/// This is why the check happens after the build rather than during discovery: before the build there
/// is nothing to read.
/// </para>
/// </remarks>
internal static class XUnitVersion
{
    /// <summary>The assembly every xUnit v3-family test application loads.</summary>
    private const string CoreAssembly = "xunit.v3.core.dll";

    /// <summary>The only major version KillMutants supports.</summary>
    public const int Supported = 4;

    /// <summary>
    /// The xUnit version <paramref name="outputDirectory"/> will run on, or null when that cannot be
    /// established.
    /// </summary>
    public static Version? In(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string path = Path.Combine(outputDirectory, CoreAssembly);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return AssemblyName.GetAssemblyName(path).Version;
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }

    /// <summary>
    /// Why <paramref name="outputDirectory"/> is not a supported test application, or null when it is.
    /// </summary>
    /// <remarks>
    /// Three answers, deliberately distinguished. A supported version runs. An earlier one is refused
    /// by name, so the message says what was found rather than leaving the user to guess why a
    /// project that clearly uses xUnit is rejected. And a version that cannot be read at all is also
    /// refused: the runner's command line is version-specific, so guessing would risk misreading a
    /// run's result - and an output directory without this assembly could not have run its tests
    /// anyway.
    /// </remarks>
    public static string? WhyUnsupported(string outputDirectory) => In(outputDirectory) switch
    {
        { Major: Supported } => null,

        { } found =>
            $"it runs on xUnit {found.ToString(3)}. KillMutants supports xUnit {Supported} only - " +
            "the xunit.v3 package family at version 4 - and deliberately nothing earlier.",

        null =>
            $"KillMutants could not find '{CoreAssembly}' in '{outputDirectory}', so it cannot " +
            $"confirm the project runs on xUnit {Supported}. Only that version is supported, and " +
            "running the tests without knowing which runner will read the results risks misreporting " +
            "them.",
    };
}
