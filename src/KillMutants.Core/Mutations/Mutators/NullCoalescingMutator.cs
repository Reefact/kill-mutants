using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Drops the fallback of a null-coalescing expression, <c>a ?? b</c> becoming <c>a</c>, and swaps
/// its operands, <c>a ?? b</c> becoming <c>b ?? a</c>, where the swap can be asked honestly.
/// </summary>
/// <remarks>
/// <para>
/// A surviving mutant of the first kind says no test ever reaches the expression with a null left
/// operand, so the fallback is unverified - one of the cheapest ways to find a default nobody
/// exercises. The mirror mutation, keeping only <c>b</c>, is deliberately not produced: it discards
/// the left operand and with it any side effect, which turns a missing-coverage signal into an
/// unrelated behaviour change.
/// </para>
/// <para>
/// The swap asks what neither of those can: which operand wins when both are set. Measured on
/// Reefact/slugger (#17), whose options resolve through a chain of
/// <c>commandLine.X ?? saved.X</c>: Stryker's swap survived on thirteen of fifteen of them, because
/// nothing asserted that a flag beat a saved setting, and the tests written to kill them found a
/// real precedence defect. Dropping the fallback could not have pointed at it - the same tests
/// kill that mutant trivially.
/// </para>
/// </remarks>
internal sealed class NullCoalescingMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("NullCoalescing");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not BinaryExpressionSyntax coalesce ||
            !coalesce.IsKind(SyntaxKind.CoalesceExpression) ||
            semanticModel.GetTypeInfo(coalesce).ConvertedType is not { } expected ||
            expected.TypeKind == TypeKind.Error)
        {
            yield break;
        }

        if (LeftFitsWhereTheWholeExpressionStood(coalesce, expected, semanticModel))
        {
            yield return new MutationCandidate(
                Name, coalesce, coalesce.Left.WithTriviaFrom(coalesce));
        }

        if (!CanBeSwapped(coalesce, semanticModel))
        {
            yield break;
        }

        BinaryExpressionSyntax swapped = Swapped(coalesce);

        if (SwapFits(swapped, coalesce, expected, semanticModel))
        {
            yield return new MutationCandidate(Name, coalesce, swapped);
        }
    }

    /// <summary>
    /// True when the left operand on its own converts to whatever the surrounding code expected.
    /// </summary>
    /// <remarks>
    /// The point of <c>??</c> is often to remove nullability, and there the left operand does not fit:
    /// <c>int total = count ?? 0</c> mutated to <c>int total = count</c> is a hard error, and
    /// <c>string s = name ?? ""</c> only survives because the nullable warning is not an error here.
    /// Classifying the conversion the compiler would have to make answers both cases in one rule,
    /// including the widening ones - <c>object o = text ?? fallback</c> stays mutable.
    /// </remarks>
    private static bool LeftFitsWhereTheWholeExpressionStood(
        BinaryExpressionSyntax coalesce,
        ITypeSymbol expected,
        SemanticModel semanticModel)
    {
        Conversion conversion = semanticModel.ClassifyConversion(coalesce.Left, expected);

        return conversion.Exists && conversion.IsImplicit;
    }

    /// <summary>True when the swap asks a question and loses nothing to ask it.</summary>
    /// <remarks>
    /// <para>
    /// The left operand has to be one that can do nothing but read. <c>b ?? a</c> evaluates
    /// <c>a</c> only when <c>b</c> is null, so swapping <c>Advance() ?? fallback</c> would skip the
    /// call whenever the fallback is set - the side effect the first mutation refuses to drop,
    /// dropped by the second. A call, an index or an assignment is therefore refused, and so is
    /// any property whose getter is code: see <see cref="OnlyReads"/>.
    /// </para>
    /// <para>
    /// Three right operands are refused for what they would make of the mutant. The same expression
    /// on both sides swaps into the original. A <c>null</c> or <c>default</c> fallback swaps into
    /// <c>null ?? a</c>, which is <c>a</c> - the mutant the first rule already produced, judged
    /// twice. And a right operand that is itself a <c>??</c> or a <c>throw</c> cannot stand on the
    /// left as it is: <c>x ?? y ?? a</c> reads back as <c>x ?? (y ?? a)</c>, so the report would
    /// show a mutant that is not the one compiled.
    /// </para>
    /// <para>
    /// Nothing on the left is refused too, and review found why: <c>null ?? s</c> and
    /// <c>s ?? null</c> are both <c>s</c>, null or not, so the swap is a mutant no test could kill.
    /// It compiles to different instructions all the same, which is why the corpus that compares
    /// emitted programs cannot see it and the rule has to.
    /// </para>
    /// </remarks>
    private static bool CanBeSwapped(
        BinaryExpressionSyntax coalesce,
        SemanticModel semanticModel) =>
        OnlyReads(coalesce.Left, semanticModel) &&
        !IsNothing(coalesce.Left) &&
        !IsNothing(coalesce.Right) &&
        !coalesce.Right.IsKind(SyntaxKind.CoalesceExpression) &&
        !coalesce.Right.IsKind(SyntaxKind.ThrowExpression) &&
        !SyntaxFactory.AreEquivalent(coalesce.Left, coalesce.Right);

    /// <summary>True for an operand that can only be null: <c>null</c> or a default.</summary>
    private static bool IsNothing(ExpressionSyntax operand) =>
        operand.IsKind(SyntaxKind.NullLiteralExpression) ||
        operand.IsKind(SyntaxKind.DefaultLiteralExpression) ||
        operand.IsKind(SyntaxKind.DefaultExpression);

    /// <summary>True when evaluating the expression can only read a value already stored.</summary>
    /// <remarks>
    /// Review found the hole in taking a property at its word: a getter is code, and can count,
    /// consume or load whatever it returns, so <c>obj.Property ?? fallback</c> swapped would skip
    /// that code whenever the fallback is set. What is accepted is only what the compiler itself
    /// stores - a local, a parameter, a field, and a property the compiler implements as one - each
    /// link of a member access included.
    /// </remarks>
    private static bool OnlyReads(ExpressionSyntax expression, SemanticModel semanticModel) =>
        expression switch
        {
            ThisExpressionSyntax or LiteralExpressionSyntax => true,
            IdentifierNameSyntax name => IsStored(semanticModel.GetSymbolInfo(name).Symbol),
            MemberAccessExpressionSyntax member
                when member.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
                IsStored(semanticModel.GetSymbolInfo(member).Symbol) &&
                OnlyReads(member.Expression, semanticModel),
            ParenthesizedExpressionSyntax parenthesized =>
                OnlyReads(parenthesized.Expression, semanticModel),
            _ => false,
        };

    /// <summary>True when reading the symbol runs no code.</summary>
    /// <remarks>
    /// A type or a namespace only appears as the receiver of a static member, and evaluates
    /// nothing at all.
    /// </remarks>
    private static bool IsStored(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol or IParameterSymbol or IFieldSymbol or INamespaceOrTypeSymbol => true,
        IPropertySymbol property => IsImplementedByTheCompiler(property),
        _ => false,
    };

    /// <summary>
    /// True for a property the compiler implements as a field: declared in this compilation with no
    /// accessor body, and nothing able to put code behind it.
    /// </summary>
    /// <remarks>
    /// <c>{ get; init; }</c> and a record's positional parameter qualify; a getter with a body, an
    /// expression body or the <c>field</c> keyword does not. Neither does a property that is
    /// virtual, abstract, an override or partial, since another declaration decides what its
    /// getter runs - nor one from another assembly, whose declaration cannot be read here at all.
    /// </remarks>
    private static bool IsImplementedByTheCompiler(IPropertySymbol property)
    {
        if (property.IsAbstract || property.IsVirtual || property.IsOverride || property.IsExtern ||
            property.IsPartialDefinition || property.PartialDefinitionPart is not null ||
            property.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        return property.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() switch
        {
            PropertyDeclarationSyntax declared =>
                declared.ExpressionBody is null &&
                declared.AccessorList is { } accessors &&
                accessors.Accessors.All(accessor =>
                    accessor.Body is null && accessor.ExpressionBody is null),
            ParameterSyntax => true,
            _ => false,
        });
    }

    /// <summary>
    /// The two operands exchanged, as a new coalesce node - the RB-001 rule, for a family that
    /// swaps operands rather than operators.
    /// </summary>
    private static BinaryExpressionSyntax Swapped(BinaryExpressionSyntax coalesce) =>
        SyntaxFactory
            .BinaryExpression(
                SyntaxKind.CoalesceExpression,
                coalesce.Right.WithTriviaFrom(coalesce.Left),
                coalesce.OperatorToken,
                coalesce.Left.WithTriviaFrom(coalesce.Right))
            .WithTriviaFrom(coalesce);

    /// <summary>
    /// True when the swapped expression binds, and converts to what the original stood for.
    /// </summary>
    /// <remarks>
    /// The swap has a type question of its own, and it is the reverse of the first mutation's. The
    /// left operand of <c>??</c> has to be able to be null, so <c>count ?? 0</c> cannot become
    /// <c>0 ?? count</c> at all: the compiler refuses it, and asking it is what keeps the mutant
    /// from being generated (RB-011). What binds then has to fit where the original stood, by the
    /// same conversion rule as the first mutation.
    /// </remarks>
    private static bool SwapFits(
        BinaryExpressionSyntax swapped,
        BinaryExpressionSyntax original,
        ITypeSymbol expected,
        SemanticModel semanticModel)
    {
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            original.SpanStart, swapped, SpeculativeBindingOption.BindAsExpression).Type;

        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        Conversion conversion = semanticModel.Compilation.ClassifyConversion(type, expected);

        return conversion.Exists && conversion.IsImplicit;
    }
}
