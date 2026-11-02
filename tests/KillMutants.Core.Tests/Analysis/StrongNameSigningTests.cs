using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using KillMutants.Analysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// Regression tests for RB-035. A strong-named project's command line names its key file, and
/// the compilation rebuilt from it has to be able to sign with it - as csc can - or the baseline
/// fails with CS7027 and the whole project is set aside before a mutant is tested.
/// </summary>
public sealed class StrongNameSigningTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("killmutants-signing-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// The shape found on jbogard/MediatR: the key file sits above the project and is named
    /// relative to it, so the provider has to resolve it against the project's directory.
    /// </summary>
    [Fact]
    public void A_project_signed_with_a_key_file_above_it_emits_a_signed_assembly()
    {
        string project = Directory.CreateDirectory(Path.Combine(_root, "src", "Sample")).FullName;
        File.WriteAllText(Path.Combine(project, "Ages.cs"), "public static class Ages { public static bool IsAdult(int age) => age >= 18; }");
        File.WriteAllBytes(Path.Combine(_root, "Sample.snk"), NewKeyPair());

        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            ["/target:library", "/out:Sample.dll", "/keyfile:../../Sample.snk",
             $"/reference:{typeof(object).Assembly.Location}", "Ages.cs"],
            baseDirectory: project,
            sdkDirectory: null);

        EmitOutcome outcome = ProjectCompilation.Create(parsed, project).EmitBaseline();

        Assert.True(outcome.Success, outcome.Diagnostics);
        Assert.False(PublicKeyOf(outcome.Assembly!).IsNil);
    }

    /// <summary>
    /// The key is read on every emit, so it is an input of the compilation: a series told to ignore
    /// an untracked <c>*.snk</c> has to see it to refuse (DEC0025) - review of #86.
    /// </summary>
    [Fact]
    public void The_key_file_above_the_project_is_an_input_of_the_compilation()
    {
        string project = Directory.CreateDirectory(Path.Combine(_root, "src", "Sample")).FullName;
        File.WriteAllText(Path.Combine(project, "Ages.cs"), "public static class Ages { }");
        string key = Path.Combine(_root, "Sample.snk");
        File.WriteAllBytes(key, NewKeyPair());

        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            ["/target:library", "/out:Sample.dll", "/keyfile:../../Sample.snk",
             $"/reference:{typeof(object).Assembly.Location}", "Ages.cs"],
            baseDirectory: project,
            sdkDirectory: null);

        Assert.Contains(Path.GetFullPath(key), ProjectCompilation.Create(parsed, project).InputFiles);
    }

    /// <summary>A key pair in the format a <c>.snk</c> file holds, which <c>sn -k</c> writes.</summary>
    private static byte[] NewKeyPair()
    {
        using var rsa = new RSACryptoServiceProvider(2048);

        return rsa.ExportCspBlob(includePrivateParameters: true);
    }

    private static BlobHandle PublicKeyOf(byte[] assembly)
    {
        using var reader = new PEReader(new MemoryStream(assembly));

        return reader.GetMetadataReader().GetAssemblyDefinition().PublicKey;
    }
}
