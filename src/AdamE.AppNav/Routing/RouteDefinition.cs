namespace AdamE.AppNav.Routing;

/// <summary>
/// One mapping in a <see cref="RouteTable"/>: a route type and the template that produces it.
/// </summary>
/// <remarks>
/// Definitions are made by <see cref="RouteTableBuilder.Map{TRoute}(string, Func{RouteMatchContext, TRoute})"/> and
/// are immutable.
/// </remarks>
public sealed class RouteDefinition
{
    internal RouteDefinition(Type routeType, RouteTemplate template, Func<RouteMatchContext, AppRoute> createRoute)
    {
        RouteType = routeType;
        Template = template;
        CreateRoute = createRoute;
    }

    /// <summary>
    /// Gets the type of route that this definition produces, the type argument given to
    /// <see cref="RouteTableBuilder.Map{TRoute}(string, Func{RouteMatchContext, TRoute})"/>.
    /// </summary>
    public Type RouteType { get; }

    /// <summary>
    /// Gets the template that a path must match for this definition to produce a route.
    /// </summary>
    public RouteTemplate Template { get; }

    internal Func<RouteMatchContext, AppRoute> CreateRoute { get; }
}
