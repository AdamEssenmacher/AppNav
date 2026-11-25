using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

/// <summary>
/// What a route's create function receives when its template matches a URI: the URI, the values the template
/// captured from its path, and the values of its query.
/// </summary>
/// <remarks>
/// A context is valid only during the call to the create function that receives it. Do not store it; build the route
/// from it and let it go.
/// </remarks>
public sealed class RouteMatchContext
{
    // Where the route table found the query, -1 for Uri.Query, and its values once a create function first asks for
    // one, so that a match whose create function never asks pays nothing for the query.
    private readonly int _queryStart;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _queryValues;

    internal RouteMatchContext(Uri sourceUri, IReadOnlyDictionary<string, string> pathValues, int queryStart)
    {
        SourceUri = sourceUri;
        PathValues = pathValues;
        _queryStart = queryStart;
    }

    /// <summary>
    /// Gets the URI that was passed to <see cref="RouteTable.Match(Uri)"/>.
    /// </summary>
    /// <remarks>
    /// A route carries application identity only, so build it from the captured values rather than from parts of the
    /// URI that the table does not read, such as its host.
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

    /// <summary>
    /// Gets the first value the query of the URI gives for a name, if it gives any.
    /// </summary>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the first value given for <paramref name="name"/>, decoded,
    /// which is empty for a name written without a value, as in <c>?flag</c> or <c>?flag=</c>; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the query has the name; <see langword="false"/> when it does not, or there is no
    /// query.
    /// </returns>
    /// <remarks>
    /// The query is <see cref="Uri.Query"/> as <see cref="Uri"/> reports it for an absolute URI, and the original
    /// text for a relative one, as described for <see cref="RouteTable.Match(Uri)"/>. It is split into pairs on
    /// <c>&amp;</c> and decoded as form data, so <c>+</c> is a space. Use <see cref="GetQueryValues(string)"/> for
    /// every value of a name that can repeat.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetQuery(string name, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (QueryValues.TryGetValue(name, out IReadOnlyList<string>? values))
        {
            value = values[0];
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Gets every value the query of the URI gives for a name, in the order they appear.
    /// </summary>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <returns>
    /// The values, decoded, including repeated and empty ones; an empty list when the query does not have the name, or
    /// there is no query. The list cannot be changed.
    /// </returns>
    /// <remarks>The query is read and decoded as described for <see cref="TryGetQuery(string, out string)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> GetQueryValues(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!QueryValues.TryGetValue(name, out IReadOnlyList<string>? values))
            return ReadOnlyCollection<string>.Empty;

        // The parsed lists are arrays or lists that later calls share, so each call wraps its list rather than letting
        // a cast change what the next call returns. Any other list would be a change to QueryString.Parse that this
        // must not hand out unwrapped.
        return values switch
        {
            string[] array => Array.AsReadOnly(array),
            List<string> list => list.AsReadOnly(),
            _ => throw new UnreachableException("QueryString.Parse returned a list of an unexpected type."),
        };
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues => _queryValues ??= ParseQuery();

    private IReadOnlyDictionary<string, IReadOnlyList<string>> ParseQuery()
    {
        if (_queryStart < 0)
            return QueryString.Parse(SourceUri.Query);

        // The query of a relative URI ends at the first '#' after its '?', and is parsed from the text in place.
        ReadOnlySpan<char> query = SourceUri.OriginalString.AsSpan(_queryStart);
        int fragment = query.IndexOf('#');
        return QueryString.Parse(fragment < 0 ? query : query[..fragment]);
    }
}
