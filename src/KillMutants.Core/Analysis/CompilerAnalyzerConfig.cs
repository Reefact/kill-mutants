using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KillMutants.Analysis;

/// <summary>
/// Exposes the <c>.editorconfig</c> and <c>.globalconfig</c> files the compiler was given, so that
/// generators reading MSBuild properties see the same values they would during a real build.
/// </summary>
/// <remarks>
/// The SDK writes build properties a generator may depend on - <c>RootNamespace</c>,
/// <c>ProjectDir</c>, feature switches - into a generated global config passed on the command line
/// as <c>/analyzerconfig:</c>. Reading the real files is both simpler and more faithful than
/// reconstructing those values from MSBuild.
/// </remarks>
internal sealed class CompilerAnalyzerConfig : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigSet _configSet;
    private readonly AnalyzerConfigOptions _globalOptions;

    private CompilerAnalyzerConfig(AnalyzerConfigSet configSet)
    {
        _configSet = configSet;
        _globalOptions = new Options(configSet.GlobalConfigOptions.AnalyzerOptions);
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    /// <summary>Reads every analyzer config file named on the compiler command line.</summary>
    public static CompilerAnalyzerConfig LoadFrom(IEnumerable<string> analyzerConfigPaths)
    {
        ArgumentNullException.ThrowIfNull(analyzerConfigPaths);

        ImmutableArray<AnalyzerConfig> configs =
        [
            .. analyzerConfigPaths
                .Where(File.Exists)
                .Select(path => AnalyzerConfig.Parse(File.ReadAllText(path), path)),
        ];

        return new CompilerAnalyzerConfig(AnalyzerConfigSet.Create(configs));
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        return new Options(_configSet.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions);
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        ArgumentNullException.ThrowIfNull(textFile);

        return new Options(_configSet.GetOptionsForSourcePath(textFile.Path).AnalyzerOptions);
    }

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);

        public override IEnumerable<string> Keys => values.Keys;
    }
}
