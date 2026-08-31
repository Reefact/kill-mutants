namespace KillMutants.Projects;

/// <summary>The project whose code is mutated.</summary>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c>.</param>
/// <param name="ProjectDirectory">The directory containing it, used to resolve relative compiler paths.</param>
/// <param name="AssemblyFileName">The file name of the assembly it produces, e.g. <c>Sample.Library.dll</c>.</param>
public sealed record ProjectUnderTest(string ProjectPath, string ProjectDirectory, string AssemblyFileName)
{
    /// <summary>The project name, for display.</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);
}
