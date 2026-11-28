namespace AdamE.AppNav.State;

/// <summary>
/// One occurrence of a route in the navigation state: a route, the <see cref="RouteMetadata"/> that travels with it,
/// and an id that names this occurrence. A route entry is a leaf of the navigation state.
/// </summary>
/// <remarks>
/// <para>
/// The id tells this occurrence from another of the same route, so that the same route shown twice is two entries;
/// the app chooses it. The id is kept exactly as given, with any leading or trailing white space, and compared
/// ordinally and case-sensitively. White space is what <see cref="char.IsWhiteSpace(char)"/> says it is, so an id of
/// other characters that cannot be seen, such as a zero-width space, is accepted.
/// </para>
/// <para>
/// An entry is immutable: its properties never hold <see langword="null"/> and cannot be set. So an entry is safe to
/// share across threads, provided its route and the values of its metadata are immutable too, as a route whose
/// members cannot be set is. An entry compares and hashes by its route and by the values of its metadata, so changing
/// a member of a route that allows it, or a metadata value that can be changed, changes what the entry equals, and
/// an entry kept in a set or as a dictionary key is then no longer found.
/// </para>
/// <para>
/// Two entries are equal when their ids are equal, their routes are equal and their metadata are equal, as
/// <see cref="RouteMetadata"/> compares; <c>==</c> and <c>!=</c> agree with <see cref="Equals(RouteEntry)"/>. An
/// entry's <see cref="object.ToString"/>
/// does not print its id, its route or its metadata, and the exceptions its constructors throw do not contain its id
/// or anything from its route, since an id is often built from route values, which can come from a link.
/// </para>
/// </remarks>
public sealed class RouteEntry : IEquatable<RouteEntry>
{
    /// <summary>
    /// Initializes an entry for a route with no metadata: its <see cref="Metadata"/> has no entries.
    /// </summary>
    /// <param name="id">
    /// The id of the entry, which tells this occurrence of the route from another. Any text that is not empty and not
    /// only white space.
    /// </param>
    /// <param name="route">The route.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="id"/> or <paramref name="route"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or only white space.</exception>
    public RouteEntry(string id, AppRoute route)
        : this(id, route, RouteMetadata.Empty)
    {
    }

    /// <summary>
    /// Initializes an entry for a route with the given metadata.
    /// </summary>
    /// <param name="id">
    /// The id of the entry, which tells this occurrence of the route from another. Any text that is not empty and not
    /// only white space.
    /// </param>
    /// <param name="route">The route.</param>
    /// <param name="metadata">The metadata that travels with the route.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="id"/>, <paramref name="route"/> or <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or only white space.</exception>
    public RouteEntry(string id, AppRoute route, RouteMetadata metadata)
    {
        // The messages of these exceptions do not contain the id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(metadata);

        Id = id;
        Route = route;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the id that tells this occurrence of the route from another, exactly as it was given.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the route.
    /// </summary>
    public AppRoute Route { get; }

    /// <summary>
    /// Gets the metadata that travels with the route: values that are not part of the route's identity.
    /// </summary>
    public RouteMetadata Metadata { get; }

    /// <summary>
    /// Determines whether this entry equals another: whether their ids are equal, compared ordinally and
    /// case-sensitively, their routes are equal and their metadata are equal.
    /// </summary>
    /// <param name="other">The entry to compare with.</param>
    /// <returns><see langword="true"/> when the entries are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(RouteEntry? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return string.Equals(Id, other.Id, StringComparison.Ordinal)
            && Route.Equals(other.Route)
            && Metadata.Equals(other.Metadata);
    }

    /// <summary>
    /// Determines whether this entry equals an object: whether it is an entry with an equal id, route and metadata.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal entry; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as RouteEntry);
    }

    /// <summary>
    /// Returns a hash code that agrees with <see cref="Equals(RouteEntry)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Id), Route, Metadata);
    }

    /// <summary>
    /// Determines whether two entries are equal, as <see cref="Equals(RouteEntry)"/> does; two that are both
    /// <see langword="null"/> are equal.
    /// </summary>
    /// <param name="left">The first to compare.</param>
    /// <param name="right">The second to compare.</param>
    /// <returns><see langword="true"/> when they are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RouteEntry? left, RouteEntry? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Determines whether two entries are not equal: the opposite of the equality operator.
    /// </summary>
    /// <param name="left">The first to compare.</param>
    /// <param name="right">The second to compare.</param>
    /// <returns><see langword="true"/> when they are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RouteEntry? left, RouteEntry? right)
    {
        return !(left == right);
    }
}
