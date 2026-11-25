using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// A path value writer for values of type TValue, which RouteFormatBuilder.Path has checked is a built-in type, an enum
// or the nullable form of one.
internal sealed class TypedPathValueWriter<TRoute, TValue> : PathValueWriter<TRoute>
    where TRoute : AppRoute
{
    private readonly Func<TRoute, TValue> _value;

    // Null for a built-in type, which RouteConstraint.TryFormat writes without a codec.
    private readonly ValueCodec<TValue>? _codec;

    public TypedPathValueWriter(string name, Func<TRoute, TValue> value)
        : this(name, value, null)
    {
    }

    private TypedPathValueWriter(string name, Func<TRoute, TValue> value, ValueCodec<TValue>? codec)
        : base(name)
    {
        _value = value;
        _codec = codec;
    }

    public override string NoTextReason => _codec?.NoTextReason ?? RouteConstraint.NoTextReason;

    // The writer itself is shared by every table its builder builds, so binding a codec makes a new one, and a table
    // never sees a codec of another table.
    public override PathValueWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template)
    {
        if (RouteValueTypes.SupportedTypeName<TValue>() is not null)
            return this;

        if (codecs.TryGetValue(typeof(TValue), out object? codec))
            return new TypedPathValueWriter<TRoute, TValue>(Name, _value, (ValueCodec<TValue>)codec);

        Type enumType = RouteValueTypes.EnumType<TValue>()!;
        throw new InvalidOperationException(
            $"The format function of route type '{RouteValueTypes.DisplayName(typeof(TRoute))}', mapped to the " +
            $"template '{template.Value}', writes path parameter '{Name}' as type " +
            $"'{RouteValueTypes.DisplayName(typeof(TValue))}', but the enum is not registered. Register it with " +
            $"RouteTableBuilder.AddEnumValueCodec<{RouteValueTypes.DisplayName(enumType)}>() when building the " +
            "table.");
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

        return _codec is null ? RouteConstraint.TryFormat(value, out text) : _codec.TryFormat(value, out text);
    }
}
