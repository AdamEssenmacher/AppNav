namespace AdamE.AppNav;

// One entry of a RouteMetadata collection. The collection holds entries through this non-generic base, and each
// entry is a TypedRouteMetadataEntry<TValue>, so a value is only ever stored and read under a key of its own type.
internal abstract class RouteMetadataEntry
{
    // Whether other is an entry with an equal key and a value equal by EqualityComparer<TValue>.Default.
    public abstract bool EntryEquals(RouteMetadataEntry other);

    // A hash code that agrees with EntryEquals.
    public abstract int GetEntryHashCode();
}
