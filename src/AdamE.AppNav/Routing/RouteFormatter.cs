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

    // The route is of exactly the type this formatter writes, which the table has looked it up by. Returns the path
    // followed by the query, if any value is written to it, and gives the path alone too, which is the same string
    // when no query is written, so that the table checks which template the path selects without splitting the text.
    public abstract string Format(AppRoute route, out string path);
}
