using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// Writes the entry that a route request's metadata has for one key declared with RouteTableBuilder.AddQueryMetadata as
// one query pair under the key's name. The type of the value is known only to the derived class, so that a table can
// keep the writers of all its keys in one array, in the order the keys were declared.
internal abstract class QueryMetadataWriter
{
    protected QueryMetadataWriter(string name, string encodedName)
    {
        Name = name;
        EncodedName = encodedName;
        Subject = $"route metadata key '{RouteText.EscapeForMessage(name)}'";
    }

    // The key's name, which the builder and the table compare, ignoring case, with the other query names.
    public string Name { get; }

    // The name percent-encoded, once, when the key was declared, and written before the value.
    public string EncodedName { get; }

    // What a message calls the key, as in "route metadata key 'campaign'", with the name quoted so that it cannot break
    // the line it is printed on.
    public string Subject { get; }

    // The value type of the key, which a message about two keys of one name names.
    public abstract Type ValueType { get; }

    // Ends the message for a value that has no text, without naming or describing the value.
    public abstract string NoTextReason { get; }

    // Returns the writer that a table built with these codecs uses, as ValueWriter.Bind does: one for a type that is
    // not built in takes its codec now, and one whose codec is missing throws, naming the key.
    public abstract QueryMetadataWriter Bind(FrozenDictionary<Type, object> codecs);

    // Appends the pair for the metadata's entry for the key, or nothing when it has none. Returns why the value cannot
    // be written when it cannot, having appended nothing for it. An exception that a codec the application registered
    // throws propagates unchanged.
    public abstract QueryValueProblem Write(RouteMetadata metadata, ref QueryBuilder query);
}
