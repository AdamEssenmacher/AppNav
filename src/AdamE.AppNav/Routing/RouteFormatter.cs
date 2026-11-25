using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// Writes a route of one type as the path of its mapping's template, from the format function that the mapping was
// given. A definition keeps one without the type of its route, so that a table can keep them all by route type.
internal abstract class RouteFormatter
{
    // Returns the formatter that a table built with these codecs uses, or throws when the format function writes a
    // value as an enum that is not registered.
    public abstract RouteFormatter Bind(FrozenDictionary<Type, object> codecs);

    // The route is of exactly the type this formatter writes, which the table has looked it up by.
    public abstract string Format(AppRoute route);
}
