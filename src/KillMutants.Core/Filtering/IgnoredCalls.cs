using System.IO.Enumeration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Filtering;

/// <summary>
/// The calls a project has said it does not want to hear about: a mutant of such a call's statement
/// or of its arguments is generated, reported as ignored, and never tested. See DEC0026.
/// </summary>
/// <remarks>
/// <para>
/// A call is recognised by the symbol it binds to, never by the name as written - the rule the tool
/// applies to <c>[Conditional]</c>, to <c>[ExcludeFromCodeCoverage]</c> and to Linq's twins - so that
/// a method of the project's own that merely shares a name with <c>ThrowIfNull</c> is not the guard
/// the project meant. A pattern is matched against the fully qualified name of the method, or of the
/// type followed by <c>.ctor</c> for a constructor, with <c>*</c> and <c>?</c> as <c>--exclude</c>
/// has them, and case as C# has it. So a bare <c>ThrowIfNull</c> recognises nothing, and the report
/// names it as a rule without effect: a wide net has to be written as one, <c>*.ThrowIfNull</c>.
/// </para>
/// <para>
/// What a recognised call silences is its statement and its arguments, and nothing beyond. An
/// argument counts only through what the call makes of it, so a mutant in one is observable only
/// through a call the project does not hear. What the call is made <em>on</em> is another matter: in
/// <c>Task.FromResult(x + 1).ConfigureAwait(false)</c> the value of <c>x + 1</c> passes through
/// <c>ConfigureAwait</c> untouched and is observed after it, and a fluent chain ending in an ignored
/// <c>ToList</c> is not a chain the project asked to silence (review of #74). And the body of a
/// lambda or of a local function handed to the call is code that runs later and whose effects escape
/// the call, so the walk stops there and a rule about the call does not reach it.
/// </para>
/// </remarks>
public sealed class IgnoredCalls
{
    /// <summary>Every form of a constructor's name in a pattern, as Stryker.NET writes it.</summary>
    private const string ConstructorName = ".ctor";

    private static readonly SymbolDisplayFormat TypeName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None);

    private IgnoredCalls(IReadOnlyList<string> rules)
    {
        Rules = rules;
    }

    /// <summary>A list that silences nothing.</summary>
    public static IgnoredCalls None { get; } = new([]);

    /// <summary>The patterns, as the project wrote them, in the order it wrote them.</summary>
    public IReadOnlyList<string> Rules { get; }

    /// <summary>Builds the list, refusing a pattern the project cannot have meant.</summary>
    /// <exception cref="ArgumentException">A pattern is empty, or made only of wildcards.</exception>
    public static IgnoredCalls Of(IEnumerable<string>? rules)
    {
        string[] listed = [.. rules ?? []];

        Validate(listed);

        return listed.Length == 0 ? None : new IgnoredCalls(listed);
    }

    /// <summary>Refuses a pattern that is empty, or that would silence every call there is.</summary>
    /// <remarks>
    /// Only the two mistakes nobody can have meant are refused. A rule that recognises nothing is not
    /// one of them: it is accepted and named in the report as having no effect, which is what tells a
    /// project that a pattern no longer matches what it once did.
    /// </remarks>
    /// <exception cref="ArgumentException">One of <paramref name="rules"/> is refused; the message says why.</exception>
    public static void Validate(IEnumerable<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (string rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                throw new ArgumentException("'--ignore-calls' needs a pattern, and was given an empty one.");
            }

            if (rule.All(character => character is '*' or '?' or '.'))
            {
                throw new ArgumentException(
                    $"'--ignore-calls {rule}' would silence every call there is, which is the run switched " +
                    "off rather than a call the project does not want to hear about. Name the method, " +
                    "such as '*.ConfigureAwait' or 'System.ArgumentNullException.ThrowIfNull'.");
            }
        }
    }

    /// <summary>
    /// The rule that silences a mutant of <paramref name="node"/>, or null when none does.
    /// </summary>
    /// <param name="node">The node a mutator proposes to change.</param>
    /// <param name="semanticModel">The model the node's tree was bound with.</param>
    public string? RuleSilencing(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (Rules.Count == 0)
        {
            return null;
        }

        // A statement mutator proposes the statement, whose call sits beneath it rather than above:
        // the walk starts from that call, so the statement is silenced by the call it is made of.
        SyntaxNode start = node is ExpressionStatementSyntax statement ? Call(statement.Expression) : node;
        SyntaxNode? reachedThrough = null;

        foreach (SyntaxNode ancestor in start.AncestorsAndSelf())
        {
            // Code handed to a call, not code of the call: a rule about the call stops here.
            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return null;
            }

            // The call itself, or a node reached through its argument list - never through what it
            // is called on, which the call passes along rather than consumes.
            if (IsACall(ancestor) &&
                reachedThrough is null or ArgumentListSyntax &&
                semanticModel.GetSymbolInfo(ancestor).Symbol is IMethodSymbol method &&
                RuleMatching(QualifiedName(method)) is { } rule)
            {
                return rule;
            }

            reachedThrough = ancestor;
        }

        return null;
    }

    /// <summary>
    /// A call, an object created, or a constructor's <c>base(...)</c> or <c>this(...)</c>, which
    /// binds to the same constructor that <c>new</c> does (review of #74).
    /// </summary>
    private static bool IsACall(SyntaxNode node) =>
        node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax;

    /// <summary>
    /// The name a pattern is matched against: the method's containing type, fully qualified and
    /// without type arguments, then the method's name - or <c>.ctor</c> for a constructor.
    /// </summary>
    /// <remarks>
    /// Without type arguments, so that <c>Task&lt;TResult&gt;.ConfigureAwait</c> and
    /// <c>Task.ConfigureAwait</c> read the same and one <c>*.ConfigureAwait</c> covers both. An
    /// extension method is named by the static class that declares it, which is where a reader
    /// looks it up.
    /// </remarks>
    public static string QualifiedName(IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(method);

        IMethodSymbol declared = method.ReducedFrom ?? method.OriginalDefinition;
        string type = declared.ContainingType?.ToDisplayString(TypeName) ?? string.Empty;
        string name = declared.MethodKind is MethodKind.Constructor ? ConstructorName : "." + declared.Name;

        return type + name;
    }

    private string? RuleMatching(string qualifiedName) =>
        Rules.FirstOrDefault(rule => FileSystemName.MatchesSimpleExpression(rule, qualifiedName, ignoreCase: false));

    /// <summary>The call an expression statement is made of, through <c>await</c> and <c>?.</c>.</summary>
    private static SyntaxNode Call(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaited => Call(awaited.Expression),
        ConditionalAccessExpressionSyntax access => Call(access.WhenNotNull),
        _ => expression,
    };
}
