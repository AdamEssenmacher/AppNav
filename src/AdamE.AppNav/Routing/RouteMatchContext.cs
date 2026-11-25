using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

/// <summary>
/// What a route's create function receives when its template matches a URI: the URI and the values the template
/// captured from its path.
/// </summary>
/// <remarks>
/// A context is valid only during the call to the create function that receives it. Do not store it; build the route
/// from it and let it go.
/// </remarks>
public sealed class RouteMatchContext
{
    internal RouteMatchContext(Uri sourceUri, IReadOnlyDictionary<string, string> pathValues)
    {
        SourceUri = sourceUri;
        PathValues = pathValues;
    }

    /// <summary>
    /// Gets the URI that was passed to <see cref="RouteTable.Match(Uri)"/>.
    /// </summary>
    /// <remarks>
    /// A route carries application identity only, so build it from the captured values rather than from parts of the
    /// URI that the table ignores, such as its host.
    /// </remarks>
    public Uri SourceUri { get; }

    /// <summary>
    /// Gets the values the template's parameters captured from the path, as returned by
    /// <see cref="RouteTemplate.Match(string)"/>: keyed by parameter name as written in the template and looked up
    /// without regard to case, each the decoded path text. An optional parameter that the path leaves out has no
    /// entry, and neither has a catch-all parameter when nothing is left of the path. Treat it as read-only.
    /// </summary>
    public IReadOnlyDictionary<string, string> PathValues { get; }

    /// <summary>
    /// Gets the value a path parameter captured.
    /// </summary>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <returns>The captured value, decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that
    /// the path left out. Use <see cref="TryGetPath(string, out string)"/> for a parameter that can be left out.
    /// </exception>
    public string Path(string name)
    {
        return TryGetPath(name, out string? value) ? value : throw new KeyNotFoundException(
            $"The matched path has no value for path parameter '{RouteText.EscapeForMessage(name)}': the template " +
            "has no parameter of that name, or the path left it out.");
    }

    /// <summary>
    /// Gets the value a path parameter captured, if the matched path has one.
    /// </summary>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the captured value, decoded; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the matched path has a value for the parameter; <see langword="false"/> when the
    /// template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that the
    /// path left out.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetPath(string name, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return PathValues.TryGetValue(name, out value);
    }
}
