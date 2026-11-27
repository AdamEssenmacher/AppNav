using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// A key declared with AddQueryMetadata whose value is of type TValue, which RouteTableBuilder has checked is not a
// collection. Its value is written by TypedValueText, by exactly the rules of a single query value of the type, and
// read by RouteMatchContext.TryGetQuery<TValue>, by exactly the rules of the typed read of a query value.
internal sealed class TypedQueryMetadataKey<TValue> : QueryMetadataKey
    where TValue : notnull
{
    // Null for a built-in type, as for TypedValueWriter, and before the key is bound. Writing and reading both use it.
    private readonly ValueCodec<TValue>? _codec;

    // The name of the type that a value that is not valid is reported as, which the typed read gives it too: the
    // codec's for a type with one, and the built-in name otherwise. Before the key is bound, the built-in name, or the
    // type's display name for a type that needs a codec; an unbound key is never read.
    private readonly string _typeName;

    public TypedQueryMetadataKey(RouteMetadataKey<TValue> key, string encodedName)
        : this(key, encodedName, null)
    {
    }

    private TypedQueryMetadataKey(RouteMetadataKey<TValue> key, string encodedName, ValueCodec<TValue>? codec)
        : base(key.Name, encodedName)
    {
        Key = key;
        _codec = codec;
        _typeName = codec?.TypeName
            ?? RouteValueTypes.SupportedTypeName<TValue>()
            ?? RouteValueTypes.DisplayName(typeof(TValue));
    }

    public RouteMetadataKey<TValue> Key { get; }

    public override Type ValueType => typeof(TValue);

    public override string NoTextReason => TypedValueText<TValue>.NoTextReasonOf(_codec);

    public override QueryMetadataKey Bind(FrozenDictionary<Type, object> codecs)
    {
        if (!TypedValueText<TValue>.TryFindCodec(codecs, out ValueCodec<TValue>? codec))
        {
            throw TypedValueText<TValue>.NoCodec(
                $"The {Subject}, declared with AddQueryMetadata, is written as type " +
                $"'{RouteValueTypes.DisplayName(typeof(TValue))}'");
        }

        return codec is null ? this : new TypedQueryMetadataKey<TValue>(Key, EncodedName, codec);
    }

    // An entry never holds null, so a value found is always written. An entry of a key of the same name and another
    // type is a different entry, which TryGet does not find.
    public override QueryValueProblem Write(RouteMetadata metadata, ref QueryBuilder query)
    {
        if (!metadata.TryGet(Key, out TValue? value))
            return QueryValueProblem.None;

        ValueTextProblem problem = TypedValueText<TValue>.Write(value, _codec, out string? text);
        return query.AppendValue(EncodedName, problem, text);
    }

    // The entry holds the value of the first value of the name, which the read compares ignoring case, read with the
    // codec bound above, which is the one the typed read finds among the same codecs. A parser that accepts a text but
    // gives null throws in the read, so the value found is never null.
    public override bool TryRead(RouteMatchContext context, [NotNullWhen(true)] out RouteMetadataEntry? entry)
    {
        if (!context.TryGetQuery<TValue>(Name, _codec, _typeName, out TValue? value))
        {
            entry = null;
            return false;
        }

        entry = new TypedRouteMetadataEntry<TValue>(Key, value);
        return true;
    }
}
