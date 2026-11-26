using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// A value writer for values of type TValue, which RouteFormatBuilder has checked is not a collection. Its static
// members write one value of the type, and are shared with ListQueryWriter, which writes each element of a list as
// this writes one value.
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
    public override ValueTextProblem Write(TRoute route, out string? text)
    {
        TValue value = _value(route);
        if (value is null)
        {
            text = null;
            return ValueTextProblem.None;
        }

        return Write(value, _codec, out text);
    }

    // Null for a built-in type, which needs no codec; otherwise the table's codec for the type, or an exception that
    // names the route type, the template, the subject and the registration that is missing. The nullable form of a
    // value type registered with AddValueCodec has no codec of its own, so it is written by the codec of its
    // underlying type, through a codec made for it here, once for each table, which boxes each value it writes.
    public static ValueCodec<TValue>? FindCodec(
        FrozenDictionary<Type, object> codecs,
        RouteTemplate template,
        string subject)
    {
        if (RouteValueTypes.SupportedTypeName<TValue>() is not null)
            return null;

        if (codecs.TryGetValue(typeof(TValue), out object? codec))
            return (ValueCodec<TValue>)codec;

        Type? underlying = Nullable.GetUnderlyingType(typeof(TValue));
        if (underlying is not null && codecs.TryGetValue(underlying, out codec))
            return new BoxingNullableValueCodec<TValue>((IBoxedValueCodec)codec);

        string writes =
            $"The format function of route type '{RouteValueTypes.DisplayName(typeof(TRoute))}', mapped to the " +
            $"template '{template.Value}', writes {subject} as type '{RouteValueTypes.DisplayName(typeof(TValue))}'";
        string name = RouteValueTypes.CodecTypeName<TValue>();
        if (RouteValueTypes.EnumType<TValue>() is not null)
        {
            throw new InvalidOperationException(
                $"{writes}, but the enum is not registered. Register it with " +
                $"RouteTableBuilder.AddEnumValueCodec<{name}>() when building the table, or give it a codec of its " +
                $"own with RouteTableBuilder.AddValueCodec<{name}>.");
        }

        // Only char gets here of the types that cannot have a codec, since the declarations reject collections.
        if (!RouteValueTypes.CanHaveCodec<TValue>())
        {
            throw new InvalidOperationException(
                $"{writes}, which is not supported: a char is one character of a string, so write it as a string " +
                "instead.");
        }

        throw new InvalidOperationException(
            $"{writes}, which is not a built-in type and has no codec. Register a codec with " +
            $"RouteTableBuilder.AddValueCodec<{name}> when building the table.");
    }

    // Writes a value that is not null, with the codec FindCodec returned.
    public static ValueTextProblem Write(TValue value, ValueCodec<TValue>? codec, out string? text)
    {
        if (codec is not null)
            return codec.Write(value, out text);

        return RouteConstraint.TryFormat(value, out text) ? ValueTextProblem.None : ValueTextProblem.NoText;
    }

    public static string NoTextReasonOf(ValueCodec<TValue>? codec)
    {
        return codec?.NoTextReason ?? RouteConstraint.NoTextReason;
    }
}
