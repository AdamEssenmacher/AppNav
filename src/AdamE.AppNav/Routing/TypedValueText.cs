using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// Writes one value of type TValue as text, by the codec a table has for the type or, for a built-in type, by
// RouteConstraint.TryFormat. Every writer of a single value shares it: a path value, a query value, each element of a
// list of query values and a route metadata value written as a query value, so that all are written by one rule.
internal static class TypedValueText<TValue>
{
    // True with null for a built-in type, which needs no codec, and true with the table's codec for any other type
    // that has one; false only when the type has none, and the caller then throws NoCodec, so that the text of the
    // message is made only for a type that needs it. The nullable form of a value type registered with AddValueCodec
    // has no codec of its own, so it is written by the codec of its underlying type, through a codec made for it
    // here, once for each table, which boxes each value it writes.
    public static bool TryFindCodec(FrozenDictionary<Type, object> codecs, out ValueCodec<TValue>? codec)
    {
        codec = null;
        if (RouteValueTypes.SupportedTypeName<TValue>() is not null)
            return true;

        if (codecs.TryGetValue(typeof(TValue), out object? found))
        {
            codec = (ValueCodec<TValue>)found;
            return true;
        }

        Type? underlying = Nullable.GetUnderlyingType(typeof(TValue));
        if (underlying is not null && codecs.TryGetValue(underlying, out found))
        {
            codec = new BoxingNullableValueCodec<TValue>((IBoxedValueCodec)found);
            return true;
        }

        return false;
    }

    // The exception for a type that TryFindCodec found no codec for. It starts with what writes the value, which ends
    // with the type it writes, and names the registration that is missing.
    public static InvalidOperationException NoCodec(string writes)
    {
        string name = RouteValueTypes.CodecTypeName<TValue>();
        if (RouteValueTypes.EnumType<TValue>() is not null)
        {
            return new InvalidOperationException(
                $"{writes}, but the enum is not registered. Register it with " +
                $"RouteTableBuilder.AddEnumValueCodec<{name}>() when building the table, or give it a codec of its " +
                $"own with RouteTableBuilder.AddValueCodec<{name}>.");
        }

        // Only char gets here of the types that cannot have a codec, since the declarations reject collections.
        if (!RouteValueTypes.CanHaveCodec<TValue>())
        {
            return new InvalidOperationException(
                $"{writes}, which is not supported: a char is one character of a string, so write it as a string " +
                "instead.");
        }

        return new InvalidOperationException(
            $"{writes}, which is not a built-in type and has no codec. Register a codec with " +
            $"RouteTableBuilder.AddValueCodec<{name}> when building the table.");
    }

    // Writes a value that is not null, with the codec TryFindCodec gave.
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
