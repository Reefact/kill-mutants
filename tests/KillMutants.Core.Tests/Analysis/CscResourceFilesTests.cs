using KillMutants.Analysis;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// Review of #68. A raw <c>EmbeddedResource</c> reaches the compiler as the file a person edits, so a
/// series told to ignore it would have measured an assembly built from another one (DEC0025).
/// </summary>
public class CscResourceFilesTests
{
    private static readonly string Project = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Sample.Library"));

    [Fact]
    public void A_raw_resource_is_named_as_the_file_the_project_holds()
    {
        IReadOnlyList<string> files = CscCommandLine.ResourceFiles(
            ["/out:obj/Sample.Library.dll", "/resource:Snapshots/Catalog.received.json,Sample.Library.Snapshots.Catalog.json"],
            Project);

        Assert.Equal([Path.Combine(Project, "Snapshots", "Catalog.received.json")], files);
    }

    [Theory]
    [InlineData("/res:data.txt")]
    [InlineData("-resource:data.txt,Name,private")]
    [InlineData("/linkresource:data.txt")]
    [InlineData("/LINKRES:data.txt")]
    [InlineData("/resource:\"data.txt\",Name")]
    public void Every_spelling_of_a_resource_option_is_read(string argument)
    {
        Assert.Equal([Path.Combine(Project, "data.txt")], CscCommandLine.ResourceFiles([argument], Project));
    }

    [Theory]
    [InlineData("/reference:lib.dll")]
    [InlineData("/out:obj/Sample.Library.dll")]
    [InlineData("Ages.cs")]
    public void Nothing_else_is_taken_for_a_resource(string argument)
    {
        Assert.Empty(CscCommandLine.ResourceFiles([argument], Project));
    }
}
