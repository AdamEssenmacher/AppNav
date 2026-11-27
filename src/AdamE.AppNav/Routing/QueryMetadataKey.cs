using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// One key declared with RouteTableBuilder.AddQueryMetadata: it writes the entry that a route request's metadata has for
// the key as one query pair under the key's name, and reads the entry back from the query of a URI that matched. The
// type of the value is known only to the derived class, so that a table can keep all its keys in one array, in the
// order the keys were declared.
internal abstract class QueryMetadataKey
{
    protected QueryMetadataKey(string name, string encodedName)
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

    // Returns the key that a table built with these codecs uses, as ValueWriter.Bind does: one for a type that is not
    // built in takes its codec now, and one whose codec is missing throws, naming the key.
    public abstract QueryMetadataKey Bind(FrozenDictionary<Type, object> codecs);

    // Appends the pair for the metadata's entry for the key, or nothing when it has none. Returns why the value cannot
    // be written when it cannot, having appended nothing for it. An exception that a codec the application registered
    // throws propagates unchanged.
    public abstract QueryValueProblem Write(RouteMetadata metadata, ref QueryBuilder query);

    // Gives the entry that the query of the context gives for the key, when the query has the key's name. The value is
    // read by the typed query read of the key's type, so a value that is present but not valid throws the
    // RouteValueException of that read, and any exception that a codec's parser throws propagates unchanged.
    public abstract bool TryRead(RouteMatchContext context, [NotNullWhen(true)] out RouteMetadataEntry? entry);
}
