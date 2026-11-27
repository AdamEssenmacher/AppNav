using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// The query metadata writer of a key whose value is of type TValue, which RouteTableBuilder has checked is not a
// collection. Its value is written by TypedValueText, by exactly the rules of a single query value of the type.
internal sealed class TypedQueryMetadataWriter<TValue> : QueryMetadataWriter
    where TValue : notnull
{
    // Null for a built-in type, as for TypedValueWriter, and before the writer is bound.
    private readonly ValueCodec<TValue>? _codec;

    public TypedQueryMetadataWriter(RouteMetadataKey<TValue> key, string encodedName)
        : this(key, encodedName, null)
    {
    }

    private TypedQueryMetadataWriter(RouteMetadataKey<TValue> key, string encodedName, ValueCodec<TValue>? codec)
        : base(key.Name, encodedName)
    {
        Key = key;
        _codec = codec;
    }

    public RouteMetadataKey<TValue> Key { get; }

    public override Type ValueType => typeof(TValue);

    public override string NoTextReason => TypedValueText<TValue>.NoTextReasonOf(_codec);

    public override QueryMetadataWriter Bind(FrozenDictionary<Type, object> codecs)
    {
        if (!TypedValueText<TValue>.TryFindCodec(codecs, out ValueCodec<TValue>? codec))
        {
            throw TypedValueText<TValue>.NoCodec(
                $"The {Subject}, declared with AddQueryMetadata, is written as type " +
                $"'{RouteValueTypes.DisplayName(typeof(TValue))}'");
        }

        return codec is null ? this : new TypedQueryMetadataWriter<TValue>(Key, EncodedName, codec);
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
}
