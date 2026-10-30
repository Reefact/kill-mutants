using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Moves an integer literal that names a value - a constant's, a field's, a property's - one step
/// each way: <c>n + 1</c> and <c>n - 1</c>.
/// </summary>
/// <remarks>
/// <para>
/// A threshold is asserted <em>around</em> far more often than <em>at</em>: a suite that checks "a
/// valid theme loads and an empty one does not" never notices <c>MinimumNouns = 100</c> becoming
/// <c>101</c>, and no operator family can tell it, because no operator is involved. One step each
/// way, rather than a jump to <c>0</c>, so the mutant asks about the boundary rather than about
/// whether the value is used at all - the same question <c>Comparison</c>'s boundary shift asks of
/// an operator, a side at a time. DEC0019.
/// </para>
/// <para>
/// Opt-in, outside <see cref="MutatorCatalog.Default"/>, because a literal is everywhere and most of
/// them are noise. That is also why it reaches initializers only: a named value is a decision
/// somebody made, while a <c>0</c> in a loop or an index in a body usually is not. Integers only,
/// since one step on <c>0.05m</c> is not a step.
/// </para>
/// <para>
/// It is the one family that reaches a <c>const</c>, which <see cref="MutationSite.IsObservable"/>
/// refuses for every other one: see <see cref="ReachesConstants"/>.
/// </para>
/// </remarks>
internal sealed class NumericLiteralMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("NumericLiteral");

    /// <inheritdoc />
    /// <remarks>
    /// A constant's value is copied into every assembly that reads it, so the test assembly keeps the
    /// original - but the project under test is recompiled with the mutant, and what it reads there
    /// changes. The generator lets this family in only where the project itself reads the constant:
    /// see <see cref="MutationSite.IsAConstantItsProjectReads"/>.
    /// </remarks>
    public bool ReachesConstants => true;

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
            AsInteger(literal.Token.Value) is not { } value ||
            !NamesAValue(literal))
        {
            yield break;
        }

        foreach (BigInteger moved in (BigInteger[])[value + 1, value - 1])
        {
            if (moved >= 0 && Rewrite(literal, moved, semanticModel) is { } replacement && Fits(literal, moved, semanticModel))
            {
                yield return new MutationCandidate(Name, literal, replacement);
            }
        }
    }

    /// <summary>
    /// True when the literal is part of the value a constant, a field or a property is initialized
    /// with, and not of code that runs inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parameter's default and an enum member are initializers too, and are left out: both are
    /// copied into their callers, which no recompilation of the project reaches. A local is left out
    /// unless it is a constant, because a local's value is ordinary code in a method body.
    /// </para>
    /// <para>
    /// A literal inside a pattern is left out too, although an initializer may hold one:
    /// <c>Path = home is { Length: &gt; 0 } ? home : fallback</c> names no value, and the
    /// <c>Pattern</c> family already shifts that bound. Measured on slugger, it was the one
    /// mutant in 25 that was noise rather than a missing assertion.
    /// </para>
    /// </remarks>
    private static bool NamesAValue(LiteralExpressionSyntax literal)
    {
        foreach (SyntaxNode ancestor in literal.Ancestors())
        {
            switch (ancestor)
            {
                case EqualsValueClauseSyntax initializer:
                    return initializer.Parent switch
                    {
                        PropertyDeclarationSyntax => true,
                        VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax } => true,
                        VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax local } => local.IsConst,
                        _ => false,
                    };

                case PatternSyntax:
                case AnonymousFunctionExpressionSyntax:
                case StatementSyntax:
                case MemberDeclarationSyntax:
                    return false;

                default:
                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// The literal written with its new value, or null when the new text would give it another type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type of an unsuffixed literal follows from its size, so <c>2147483647 + 1</c> is a
    /// <c>uint</c>: a mutant that changes the type changes overload resolution and arithmetic as well
    /// as the value, and asks something other than the boundary. The original suffix is kept and the
    /// text parsed back, so the compiler, not this class, says what type the new literal has.
    /// </para>
    /// <para>
    /// Under a unary minus it is the negated expression's type that must stay, not the token's. C#
    /// types <c>-2147483648</c> as an <c>int</c> although its token <c>2147483648</c> is a
    /// <c>uint</c>, and <c>-9223372036854775808</c> as a <c>long</c> over a <c>ulong</c> token, so
    /// comparing tokens refused the only neighbour of <c>int.MinValue</c> and <c>long.MinValue</c>.
    /// The rule depends on the suffix too - <c>-2147483648u</c> is a <c>long</c>, since negation
    /// promotes a <c>uint</c> - so the negated expression is bound by the compiler rather than
    /// typed by a rule restated here.
    /// </para>
    /// </remarks>
    private static LiteralExpressionSyntax? Rewrite(
        LiteralExpressionSyntax literal, BigInteger moved, SemanticModel semanticModel)
    {
        string text = literal.Token.Text;
        string suffix = text[(text.TrimEnd('u', 'U', 'l', 'L').Length)..];

        if (SyntaxFactory.ParseExpression(moved.ToString(CultureInfo.InvariantCulture) + suffix)
                is not LiteralExpressionSyntax replacement)
        {
            return null;
        }

        bool sameType = literal.Parent is PrefixUnaryExpressionSyntax negation &&
                        negation.IsKind(SyntaxKind.UnaryMinusExpression)
            ? SymbolEqualityComparer.Default.Equals(
                semanticModel.GetTypeInfo(negation).Type,
                semanticModel.GetSpeculativeTypeInfo(
                    negation.SpanStart,
                    SyntaxFactory.PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, replacement),
                    SpeculativeBindingOption.BindAsExpression).Type)
            : replacement.Token.Value?.GetType() == literal.Token.Value?.GetType();

        return sameType ? replacement.WithTriviaFrom(literal) : null;
    }

    /// <summary>
    /// True when the new value still converts to the type the literal is used as.
    /// </summary>
    /// <remarks>
    /// <c>byte b = 255</c> compiles because the constant fits a <c>byte</c>, and <c>256</c> does not:
    /// CS0031. An enum accepts the literal <c>0</c> and no other, so <c>Level l = 0</c> has no
    /// neighbour that compiles. A negated literal is checked as the negative value it is. A literal
    /// inside a larger constant that needs such a conversion - <c>byte b = 200 + 55</c> - is left
    /// alone rather than evaluated: the whole constant is what must fit, and the case is rare.
    /// </remarks>
    private static bool Fits(LiteralExpressionSyntax literal, BigInteger moved, SemanticModel semanticModel)
    {
        ExpressionSyntax used = literal.Parent is PrefixUnaryExpressionSyntax negation &&
                                negation.IsKind(SyntaxKind.UnaryMinusExpression)
            ? negation
            : literal;
        BigInteger signed = used == literal ? moved : -moved;

        if (MutationSite.OutermostConstant(used, semanticModel) is var outermost &&
            outermost != used &&
            MutationSite.ConvertsOnlyAsAConstant(outermost, semanticModel))
        {
            return false;
        }

        ITypeSymbol? target = semanticModel.GetTypeInfo(used).ConvertedType;

        if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            target = nullable.TypeArguments[0];
        }

        if (target?.TypeKind == TypeKind.Enum)
        {
            return false;
        }

        (BigInteger Min, BigInteger Max)? range = target?.SpecialType switch
        {
            SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Char => (char.MinValue, char.MaxValue),
            SpecialType.System_Int32 or SpecialType.System_IntPtr => (int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 or SpecialType.System_UIntPtr => (uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
            SpecialType.System_UInt64 => (ulong.MinValue, ulong.MaxValue),
            _ => null,
        };

        return range is not { } bounds || (signed >= bounds.Min && signed <= bounds.Max);
    }

    private static BigInteger? AsInteger(object? value) => value switch
    {
        int number => number,
        uint number => number,
        long number => number,
        ulong number => number,
        _ => null,
    };
}
