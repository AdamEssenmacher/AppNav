namespace AdamE.AppNav;

// An entry whose value has the key's type. The entry's type carries TValue, so a type test, which cannot fail,
// finds an entry for a key, and no cast is ever made.
internal sealed class TypedRouteMetadataEntry<TValue> : RouteMetadataEntry
    where TValue : notnull
{
    public TypedRouteMetadataEntry(RouteMetadataKey<TValue> key, TValue value)
    {
        Key = key;
        Value = value;
    }

    public RouteMetadataKey<TValue> Key { get; }

    public TValue Value { get; }

    public override bool EntryEquals(RouteMetadataEntry other)
    {
        return other is TypedRouteMetadataEntry<TValue> typed
            && Key.Equals(typed.Key)
            && EqualityComparer<TValue>.Default.Equals(Value, typed.Value);
    }

    public override int GetEntryHashCode()
    {
        return HashCode.Combine(Key, EqualityComparer<TValue>.Default.GetHashCode(Value));
    }
}
