using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// Writes a route of one type as the path and query of its mapping's template, from the format function that the
// mapping was given. A definition keeps one without the type of its route, so that a table can keep them all by route
// type.
internal abstract class RouteFormatter
{
    // Returns the formatter that a table built with these codecs uses, or throws when the format function writes a
    // value of a type that is not built in and has no codec.
    public abstract RouteFormatter Bind(FrozenDictionary<Type, object> codecs);

    // The names of the query values the format function declares, with Query or QueryValues, in the order declared.
    public abstract ReadOnlySpan<string> QueryNames { get; }

    // The route is of exactly the type this formatter writes, which the table has looked it up by. Returns the path
    // followed by the query, if any value is written to it, and gives the path alone too, which is the same string
    // when no query is written, so that the table checks which template the path selects without splitting the text.
    // The query is the route's own values, then the metadata's entry for each key that queryMetadata writes, in
    // their order. A value that cannot be written throws ArgumentException whose ParamName is paramName, the parameter
    // of the method the application called that holds the route.
    public abstract string Format(
        AppRoute route,
        RouteMetadata metadata,
        QueryMetadataKey[] queryMetadata,
        string paramName,
        out string path);
}
