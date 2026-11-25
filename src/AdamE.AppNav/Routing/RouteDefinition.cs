namespace AdamE.AppNav.Routing;

/// <summary>
/// One mapping in a <see cref="RouteTable"/>: a route type and the template that produces it.
/// </summary>
/// <remarks>
/// Definitions are made by the <c>Map</c> methods of <see cref="RouteTableBuilder"/> and are immutable.
/// </remarks>
public sealed class RouteDefinition
{
    internal RouteDefinition(
        Type routeType,
        RouteTemplate template,
        Func<RouteMatchContext, AppRoute> createRoute,
        RouteFormatter? formatter)
    {
        RouteType = routeType;
        Template = template;
        CreateRoute = createRoute;
        Formatter = formatter;
    }

    /// <summary>
    /// Gets the type of route that this definition produces, the type argument given to the <c>Map</c> method of
    /// <see cref="RouteTableBuilder"/> that made it.
    /// </summary>
    public Type RouteType { get; }

    /// <summary>
    /// Gets the template that a path must match for this definition to produce a route.
    /// </summary>
    public RouteTemplate Template { get; }

    internal Func<RouteMatchContext, AppRoute> CreateRoute { get; }

    // How a route of this type is written as a path of the template and a query, from the format function the mapping
    // was given, or null for a mapping without one. A table binds it to its own codecs; this one is never used to
    // write.
    internal RouteFormatter? Formatter { get; }
}
