using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

/// <summary>
/// The outcome of <see cref="RouteTable.Match(Uri)"/>: the route a URI produced, or why it produced none.
/// </summary>
/// <remarks>
/// When <see cref="IsSuccess"/> is <see langword="true"/>, <see cref="Route"/> and <see cref="Definition"/> are not
/// <see langword="null"/>. When it is <see langword="false"/>, <see cref="Route"/> is <see langword="null"/>,
/// <see cref="Diagnostics"/> has at least one entry, and <see cref="Definition"/> is the definition whose template
/// matched when there was one, as for a value that is not valid, or <see langword="null"/>.
/// <see cref="Metadata"/> is never <see langword="null"/>, and has no entries when the match failed.
/// </remarks>
public sealed class RouteMatchResult
{
    private RouteMatchResult(
        AppRoute? route,
        RouteDefinition? definition,
        RouteMetadata metadata,
        IReadOnlyList<RouteDiagnostic> diagnostics)
    {
        Route = route;
        Definition = definition;
        Metadata = metadata;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets whether the URI produced a route. When <see langword="true"/>, <see cref="Route"/> and
    /// <see cref="Definition"/> are not <see langword="null"/>.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Route), nameof(Definition))]
    public bool IsSuccess => Route is not null;

    /// <summary>
    /// Gets the route that the create function returned, or <see langword="null"/> when the match failed.
    /// </summary>
    public AppRoute? Route { get; }

    /// <summary>
    /// Gets the definition whose template matched. Not <see langword="null"/> when <see cref="IsSuccess"/> is
    /// <see langword="true"/>. When the match failed, it is the definition whose create function rejected a value
    /// with a <see cref="RouteValueException"/>, or whose template matched a URI with a route metadata value that is
    /// not valid, or <see langword="null"/> when no template matched.
    /// </summary>
    public RouteDefinition? Definition { get; }

    /// <summary>
    /// Gets the route metadata that the query of the URI gives for the keys the table declares with
    /// <see cref="RouteTableBuilder.AddQueryMetadata{TValue}(RouteMetadataKey{TValue})"/>: an entry for each declared
    /// key whose name the query has, read from the first value of that name. Never <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// It is <see cref="RouteMetadata.Empty"/> when the match failed, when the table declares no keys, and when the
    /// query has none of their names. It has no entry for a query name that no declared key has.
    /// </remarks>
    public RouteMetadata Metadata { get; }

    /// <summary>
    /// Gets the diagnostics the match reported: at least one when it failed, saying why. Never
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<RouteDiagnostic> Diagnostics { get; }

    internal static RouteMatchResult Success(AppRoute route, RouteDefinition definition, RouteMetadata metadata)
    {
        return new RouteMatchResult(route, definition, metadata, []);
    }

    internal static RouteMatchResult Failure(RouteDiagnostic diagnostic, RouteDefinition? definition = null)
    {
        // A collection expression gives a list that cannot be changed through a cast.
        return new RouteMatchResult(null, definition, RouteMetadata.Empty, [diagnostic]);
    }
}
