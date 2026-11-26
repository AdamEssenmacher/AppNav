using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// A value writer for values of type TValue, which RouteFormatBuilder has checked is a built-in type, an enum or the
// nullable form of one. Its static members write one value of the type, and are shared with ListQueryWriter, which
// writes each element of a list as this writes one value.
internal sealed class TypedValueWriter<TRoute, TValue> : ValueWriter<TRoute>
    where TRoute : AppRoute
{
    private readonly Func<TRoute, TValue> _value;

    // Null for a built-in type, which RouteConstraint.TryFormat writes without a codec.
    private readonly ValueCodec<TValue>? _codec;

    public TypedValueWriter(string name, string subject, Func<TRoute, TValue> value)
        : this(name, subject, value, null)
    {
    }

    private TypedValueWriter(string name, string subject, Func<TRoute, TValue> value, ValueCodec<TValue>? codec)
        : base(name, subject)
    {
        _value = value;
        _codec = codec;
    }

    public override string NoTextReason => NoTextReasonOf(_codec);

    // The writer itself is shared by every table its builder builds, so binding a codec makes a new one, and a table
    // never sees a codec of another table.
    public override ValueWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template)
    {
        ValueCodec<TValue>? codec = FindCodec(codecs, template, Subject);
        return codec is null ? this : new TypedValueWriter<TRoute, TValue>(Name, Subject, _value, codec);
    }

    // A null reference and a nullable value type without a value are both null here; the test boxes nothing.
    public override bool TryWrite(TRoute route, out string? text)
    {
        TValue value = _value(route);
        if (value is null)
        {
            text = null;
            return true;
        }

        return TryFormat(value, _codec, out text);
    }

    // Null for a built-in type, which needs no codec; otherwise the table's codec for the enum, or an exception that
    // names the route type, the template and the subject when the enum is not registered.
    public static ValueCodec<TValue>? FindCodec(
        FrozenDictionary<Type, object> codecs,
        RouteTemplate template,
        string subject)
    {
        if (RouteValueTypes.SupportedTypeName<TValue>() is not null)
            return null;

        if (codecs.TryGetValue(typeof(TValue), out object? codec))
            return (ValueCodec<TValue>)codec;

        Type enumType = RouteValueTypes.EnumType<TValue>()!;
        throw new InvalidOperationException(
            $"The format function of route type '{RouteValueTypes.DisplayName(typeof(TRoute))}', mapped to the " +
            $"template '{template.Value}', writes {subject} as type " +
            $"'{RouteValueTypes.DisplayName(typeof(TValue))}', but the enum is not registered. Register it with " +
            $"RouteTableBuilder.AddEnumValueCodec<{RouteValueTypes.DisplayName(enumType)}>() when building the " +
            "table.");
    }

    // Writes a value that is not null, with the codec FindCodec returned.
    public static bool TryFormat(TValue value, ValueCodec<TValue>? codec, [NotNullWhen(true)] out string? text)
    {
        return codec is null ? RouteConstraint.TryFormat(value, out text) : codec.TryFormat(value, out text);
    }

    public static string NoTextReasonOf(ValueCodec<TValue>? codec)
    {
        return codec?.NoTextReason ?? RouteConstraint.NoTextReason;
    }
}
