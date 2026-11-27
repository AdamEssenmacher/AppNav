namespace AdamE.AppNav;

/// <summary>
/// A request for a route: the route, which says where to go and is the destination's identity, together with the
/// <see cref="RouteMetadata"/> that travels with it, which says things about this request, such as the campaign a
/// link came from.
/// </summary>
/// <remarks>
/// <para>
/// Create a request from a route and add metadata with
/// <see cref="WithMetadata{TValue}(RouteMetadataKey{TValue}, TValue)"/>, for example
/// <c>new AppRouteRequest(route).WithMetadata(campaign, "spring")</c>, and read it with
/// <see cref="RouteMetadata.TryGet{TValue}(RouteMetadataKey{TValue}, out TValue)"/> on <see cref="Metadata"/>.
/// A request is immutable: its properties never hold <see langword="null"/> and cannot be set, not even by a
/// <see langword="with"/> expression, and <see cref="WithMetadata{TValue}(RouteMetadataKey{TValue}, TValue)"/>
/// returns a new request. So a request is safe to share across threads, provided its route is immutable too, as a
/// route whose members cannot be set is.
/// </para>
/// <para>
/// Two requests are equal when their routes are equal and their metadata are equal; <c>==</c> and <c>!=</c> agree
/// with <see cref="Equals(AppRouteRequest)"/>. A record's <see cref="object.ToString"/> prints every member, so a
/// request that is logged shows all of the values of its route, but not the values of its metadata, since
/// <see cref="RouteMetadata"/> does not print its entries.
/// </para>
/// </remarks>
public sealed record AppRouteRequest
{
    /// <summary>
    /// Initializes a request for a route with no metadata: its <see cref="Metadata"/> has no entries.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> is <see langword="null"/>.</exception>
    public AppRouteRequest(AppRoute route)
        : this(route, RouteMetadata.Empty)
    {
    }

    /// <summary>
    /// Initializes a request for a route with the given metadata.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="metadata">The metadata that travels with the route.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="route"/> or <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    public AppRouteRequest(AppRoute route, RouteMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(metadata);

        Route = route;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the route: where to go, and the identity of the destination.
    /// </summary>
    public AppRoute Route { get; }

    /// <summary>
    /// Gets the metadata that travels with the route: values about this request that are not part of the route's
    /// identity.
    /// </summary>
    public RouteMetadata Metadata { get; }

    /// <summary>
    /// Returns a request for the same route with the metadata entry for a key set to a value, as
    /// <see cref="RouteMetadata.With{TValue}(RouteMetadataKey{TValue}, TValue)"/> sets it.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>
    /// A new request with this request's route, and with this request's metadata with the entry for
    /// <paramref name="key"/> set. This request is unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public AppRouteRequest WithMetadata<TValue>(RouteMetadataKey<TValue> key, TValue value)
        where TValue : notnull
    {
        return new AppRouteRequest(Route, Metadata.With(key, value));
    }
}
