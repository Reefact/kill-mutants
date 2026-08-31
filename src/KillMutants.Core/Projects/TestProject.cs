namespace KillMutants.Projects;

/// <summary>The xUnit 4 project that exercises the project under test.</summary>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c>.</param>
/// <param name="AssemblyPath">Absolute path of the built test assembly, which is also the test application.</param>
/// <param name="OutputDirectory">The build output directory, where the assembly under test must be injected.</param>
public sealed record TestProject(string ProjectPath, string AssemblyPath, string OutputDirectory)
{
    /// <summary>The project name, for display.</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);
}
