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
    /// In this version, the same route type can be mapped to more than one template. The same template cannot be
    /// mapped twice, nor two templates that rank equally and can match the same path, but that is checked by
    /// <see cref="Build"/>, not here. The order of the calls sets the order of <see cref="RouteTable.Definitions"/>,
    /// but never which mapping wins when several match, which follows the precedence of their templates as
    /// <see cref="RouteTable.Match(Uri)"/> describes.
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
    /// Its <see cref="RouteTable.Definitions"/> are in the order they were mapped, and the order in which
    /// <see cref="RouteTable.Match(Uri)"/> tries them, by precedence, is decided here, once.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Two mappings have the same template: the same number of segments and, at every position, the same kind of
    /// segment, the same decoded literal text, compared ordinally and case-sensitively, and the same constraint or
    /// none. Parameter names do not count, so <c>/a/{x}</c> and <c>/a/{y}</c> are the same. The message names both
    /// templates and both route types, the earlier mapping first: the first mapping, in the order the routes were
    /// mapped, whose template repeats an earlier one is reported, with the mapping it repeats.
    /// <para>-or-</para>
    /// Two mappings have templates that are not the same but rank equally at every position and both match some
    /// path, such as <c>/products/{id:int}</c> and <c>/products/{n:long}</c>, which both match <c>/products/0</c>, or
    /// <c>/a/{x:int?}</c> and <c>/a/{y:alpha?}</c>, which both match <c>/a</c>. The route that won for such a path
    /// would depend on the order of the mappings. The message names both templates and both route types, the earlier
    /// mapping first, and one path that both match: the first mapping, in the order the routes were mapped, that
    /// conflicts with an earlier one is reported, with the earliest it conflicts with. Templates that are the same are
    /// reported as the same, not as this. Templates that rank differently can match the same path, because precedence
    /// decides between them. In either case, the builder is not changed.
    /// </exception>
    public RouteTable Build()
    {
        return new RouteTable(_definitions.ToArray());
    }
}
