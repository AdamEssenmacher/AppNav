using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// A value writer for values of type TValue, which RouteFormatBuilder has checked is not a collection. It writes its
// value by TypedValueText, as ListQueryWriter writes each element of a list, and finds its codec by FindCodec, which
// ListQueryWriter shares.
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

    public override string NoTextReason => TypedValueText<TValue>.NoTextReasonOf(_codec);

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

        return TypedValueText<TValue>.Write(value, _codec, out text);
    }

    // The codec of the table for TValue, or an exception that names the route type, the template, the subject and the
    // registration that is missing, made only when one is.
    public static ValueCodec<TValue>? FindCodec(
        FrozenDictionary<Type, object> codecs,
        RouteTemplate template,
        string subject)
    {
        if (TypedValueText<TValue>.TryFindCodec(codecs, out ValueCodec<TValue>? codec))
            return codec;

        throw TypedValueText<TValue>.NoCodec(
            $"The format function of route type '{RouteValueTypes.DisplayName(typeof(TRoute))}', mapped to the " +
            $"template '{template.Value}', writes {subject} as type '{RouteValueTypes.DisplayName(typeof(TValue))}'");
    }
}
