using System.Reflection;
using CsCheck;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// <see cref="RunIdentity.WhyNotContinuedBy"/> compares the identity field by field, written out by
/// hand, and a field it forgets is a series that inherits verdicts across settings it was meant to
/// tell apart (DEC0015) - silently, since nothing else looks. The example tests name the fields that
/// exist today; these walk the record itself, so a field added tomorrow is held to the same rule
/// without anyone remembering to add a case for it.
/// </summary>
public class RunIdentityPropertyTests
{
    /// <summary>
    /// The fields a difference in does not refuse on its own, each with the reason. Anything else
    /// on the record must refuse when it alone changes.
    /// </summary>
    private static readonly Dictionary<string, string> NotComparedBothWays = new(StringComparer.Ordinal)
    {
        [nameof(RunIdentity.NotGenerated)] =
            "compared one way only: a project this run set aside that the earlier run generated is " +
            "refused, while one the earlier run set aside is generated here as new mutants - see " +
            "RunIdentity.WhyNotContinuedBy",
    };

    /// <summary>
    /// The fields only known once the run has discovered, built and selected, which a side that
    /// does not know them yet leaves null and is not compared on.
    /// </summary>
    private static readonly string[] LearnedLate =
    [
        nameof(RunIdentity.Scope),
        nameof(RunIdentity.Fingerprint),
        nameof(RunIdentity.TargetGraph),
        nameof(RunIdentity.TestFramework),
    ];

    private static readonly PropertyInfo[] Fields =
        typeof(RunIdentity).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static readonly Gen<string> Word = Gen.String[Gen.Char['a', 'z'], 1, 6];

    /// <summary>Distinct values, so that taking one away changes the set.</summary>
    private static readonly Gen<IReadOnlyList<string>> Words =
        Word.Array[0, 3].Select(words => (IReadOnlyList<string>)[.. words.Distinct(StringComparer.Ordinal)]);

    /// <summary>An identity a later run could continue, with every field known.</summary>
    private static readonly Gen<RunIdentity> Identities =
        from code in Gen.Select(
            Gen.String[Gen.Char["0123456789abcdef"], 8, 8], Gen.OneOf("", "src", "services/a"))
        from tool in Word
        from configuration in Gen.OneOf("Debug", "Release")
        from coverage in Gen.Bool
        from mutators in Words
        from without in Words
        from exclude in Words
        from scope in Gen.Select(Gen.OneOf("full", "since"), Word, Word, Gen.Bool)
        from fingerprint in Gen.String[Gen.Char["0123456789abcdef"], 16, 16]
        from graph in Word.Array[1, 3]
        from testFramework in Gen.OneOf("xUnit 4.0.0", "xUnit 4.0.2")
        from with in Words
        from targetFramework in Gen.OneOf("net10.0", "net11.0")
        from ignoreCalls in Words
        select new RunIdentity(
            new CodeState(code.Item1, IsExact: true, code.Item2),
            tool,
            configuration,
            coverage,
            mutators,
            without,
            exclude,
            new PlannedScope(scope.Item1, scope.Item2, scope.Item3, scope.Item4),
            fingerprint,
            graph,
            testFramework,
            with,
            targetFramework,
            NotGenerated: null,
            ignoreCalls);

    [Fact]
    public void An_identity_continues_itself_and_one_whose_lists_are_reordered()
    {
        Identities.Sample(
            identity =>
            {
                RunIdentity reordered = identity with
                {
                    Mutators = [.. identity.Mutators.Reverse()],
                    Without = [.. identity.Without.Reverse()],
                    Exclude = [.. identity.Exclude.Reverse()],
                    With = [.. identity.With!.Reverse()],
                    IgnoreCalls = [.. identity.IgnoreCalls!.Reverse()],
                };

                Assert.Null(identity.WhyNotContinuedBy(identity));
                Assert.Null(reordered.WhyNotContinuedBy(identity));
                Assert.Null(identity.WhyNotContinuedBy(reordered));
            },
            iter: 500);
    }

    /// <summary>
    /// Every field of the record, found by reflection, and every way of changing it this test knows:
    /// changed alone, it refuses, whichever side changed - unless it is named in
    /// <see cref="NotComparedBothWays"/> with the reason.
    /// </summary>
    [Fact]
    public void Any_field_that_alone_differs_refuses_the_continuation()
    {
        Gen.Select(Identities, Gen.Int[0, 1_000]).Sample(
            (identity, choice) =>
            {
                foreach (PropertyInfo field in Fields.Where(field => !NotComparedBothWays.ContainsKey(field.Name)))
                {
                    object? varied = Varied(field.GetValue(identity), field.PropertyType, choice);
                    RunIdentity changed = With(identity, field, varied);

                    Assert.True(
                        changed.WhyNotContinuedBy(identity) is not null,
                        $"Changing {field.Name} alone was not refused: {Describe(field.GetValue(identity))} " +
                        $"became {Describe(field.GetValue(changed))}.");
                    Assert.True(
                        identity.WhyNotContinuedBy(changed) is not null,
                        $"Changing {field.Name} alone was not refused the other way round.");
                }
            },
            iter: 500);
    }

    [Fact]
    public void A_field_one_side_has_not_learned_yet_is_not_compared()
    {
        Identities.Sample(
            identity =>
            {
                foreach (string name in LearnedLate)
                {
                    PropertyInfo field = typeof(RunIdentity).GetProperty(name)!;
                    RunIdentity unknown = With(identity, field, null);

                    Assert.Null(unknown.WhyNotContinuedBy(identity));
                    Assert.Null(identity.WhyNotContinuedBy(unknown));
                }
            },
            iter: 200);
    }

    /// <summary>The lists must stay what they look like here, or the exemption is a blind spot.</summary>
    [Fact]
    public void Every_exempted_or_late_field_is_a_field_of_the_identity()
    {
        Assert.All(
            NotComparedBothWays.Keys.Concat(LearnedLate),
            name => Assert.Contains(Fields, field => field.Name == name));
    }

    /// <summary>
    /// A value of the same type that differs from <paramref name="value"/>: one word added to a list
    /// or one taken away, a character added to a text, a flag turned over, and for a record of the
    /// identity's own one of its fields changed the same way.
    /// </summary>
    /// <exception cref="InvalidOperationException">A type this test has no way to change.</exception>
    private static object? Varied(object? value, Type type, int choice)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return !(bool)value!;
        }

        if (underlying == typeof(string))
        {
            return value is string text ? text + "~" : "~";
        }

        if (underlying == typeof(IReadOnlyList<string>))
        {
            var list = (IReadOnlyList<string>?)value ?? [];

            return list.Count > 0 && choice % 2 == 0
                ? (IReadOnlyList<string>)[.. list.Skip(1)]
                : (IReadOnlyList<string>)[.. list, "~added"];
        }

        if (underlying.Namespace == typeof(RunIdentity).Namespace && value is not null)
        {
            PropertyInfo[] members = underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo member = members[choice % members.Length];

            object? varied = Varied(member.GetValue(value), member.PropertyType, choice / members.Length);

            return With(value, member, varied);
        }

        throw new InvalidOperationException(
            $"This test does not know how to change a {underlying.Name}: teach Varied, so that a field " +
            "of that type is held to the rule like the others.");
    }

    private static T With<T>(T record, PropertyInfo field, object? value)
        where T : notnull
    {
        // A record's copy, as `with` makes it, then the one field set through its init accessor.
        var copy = (T)record.GetType().GetMethod("<Clone>$")!.Invoke(record, null)!;

        field.SetValue(copy, value);

        return copy;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        IEnumerable<string> list => "[" + string.Join(", ", list) + "]",
        _ => value.ToString()!,
    };
}
