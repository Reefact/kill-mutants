using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KillMutants.Analysis;

/// <summary>An <c>AdditionalFiles</c> entry from the compiler command line.</summary>
/// <remarks>
/// Generators that read non-source inputs depend on these. Omitting them makes such a generator
/// silently contribute nothing, which surfaces later as an unexplained compile error.
/// </remarks>
internal sealed class CompilerAdditionalText(string path) : AdditionalText
{
    /// <inheritdoc />
    public override string Path { get; } = path;

    /// <inheritdoc />
    public override SourceText? GetText(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(Path);

        return SourceText.From(stream);
    }
}
