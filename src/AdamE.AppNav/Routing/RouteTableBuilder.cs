namespace AdamE.AppNav.Routing;

/// <summary>
/// Collects the mappings from route templates to route types and builds a <see cref="RouteTable"/> from them.
/// </summary>
/// <remarks>
/// A builder is not thread-safe. The tables it builds are, and do not change when the builder does.
/// </remarks>
public sealed class RouteTableBuilder
{
    private readonly List<RouteDefinition> _definitions = [];

    /// <summary>
    /// Maps a route template to a route type.
    /// </summary>
    /// <typeparam name="TRoute">The type of route that a path matching the template produces.</typeparam>
    /// <param name="template">
    /// The route template, parsed as by <see cref="RouteTemplate.Parse(string)"/>.
    /// </param>
    /// <param name="createRoute">
    /// Creates the route from what the match captured. It is called once for each URI whose path the template
    /// matches, when this mapping is the one that wins, and must return a route, not <see langword="null"/>.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// In this version, the same route type or template can be mapped more than once, and nothing is rejected when
    /// the table is built; later versions will reject some such mappings. See <see cref="RouteTable.Match(Uri)"/> for
    /// which mapping wins when several match.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/> or <paramref name="createRoute"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="template"/> is not a valid route template, for any of the reasons that
    /// <see cref="RouteTemplate.Parse(string)"/> gives. The exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>template</c>.
    /// </exception>
    public RouteTableBuilder Map<TRoute>(string template, Func<RouteMatchContext, TRoute> createRoute)
        where TRoute : AppRoute
    {
        RouteTemplate parsed = RouteTemplate.Parse(template, nameof(template));
        ArgumentNullException.ThrowIfNull(createRoute);

        // Func is covariant in its result, so the function is stored as it is, without a wrapper.
        _definitions.Add(new RouteDefinition(typeof(TRoute), parsed, createRoute));
        return this;
    }

    /// <summary>
    /// Builds a route table from the mappings made so far.
    /// </summary>
    /// <returns>
    /// A new, immutable table. Mappings made on this builder afterwards do not change it, and this method can be
    /// called again to build another table that includes them. A table with no mappings is valid and matches nothing.
    /// </returns>
    public RouteTable Build()
    {
        return new RouteTable(_definitions.ToArray());
    }
}
