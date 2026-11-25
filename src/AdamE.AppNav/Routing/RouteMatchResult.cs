using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

/// <summary>
/// The outcome of <see cref="RouteTable.Match(Uri)"/>: the route a URI produced, or why it produced none.
/// </summary>
/// <remarks>
/// When <see cref="IsSuccess"/> is <see langword="true"/>, <see cref="Route"/> and <see cref="Definition"/> are not
/// <see langword="null"/>. When it is <see langword="false"/>, <see cref="Route"/> is <see langword="null"/> and
/// <see cref="Diagnostics"/> has at least one entry.
/// </remarks>
public sealed class RouteMatchResult
{
    private RouteMatchResult(AppRoute? route, RouteDefinition? definition, IReadOnlyList<RouteDiagnostic> diagnostics)
    {
        Route = route;
        Definition = definition;
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
    /// <see langword="true"/>.
    /// </summary>
    public RouteDefinition? Definition { get; }

    /// <summary>
    /// Gets the diagnostics the match reported: at least one when it failed, saying why. Never
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<RouteDiagnostic> Diagnostics { get; }

    internal static RouteMatchResult Success(AppRoute route, RouteDefinition definition)
    {
        return new RouteMatchResult(route, definition, []);
    }

    internal static RouteMatchResult Failure(RouteDiagnostic diagnostic)
    {
        // A collection expression gives a list that cannot be changed through a cast.
        return new RouteMatchResult(null, null, [diagnostic]);
    }
}
