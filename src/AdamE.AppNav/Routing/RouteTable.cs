using System.Collections.ObjectModel;

namespace AdamE.AppNav.Routing;

/// <summary>
/// Matches URIs to typed routes, using the templates mapped by a <see cref="RouteTableBuilder"/>.
/// </summary>
/// <remarks>
/// A table is immutable once built, and safe to use from several threads at once: <see cref="Match(Uri)"/> reads
/// nothing that can change. A create function that is called from several threads must itself be safe to call that
/// way.
/// </remarks>
public sealed class RouteTable
{
    // The definitions in the order they are tried, kept apart from Definitions so that match order can differ from
    // registration order. For now it is registration order.
    private readonly RouteDefinition[] _matchOrder;

    internal RouteTable(RouteDefinition[] definitions)
    {
        _matchOrder = definitions;

        // A read-only wrapper, so that a caller cannot cast Definitions back to the array and change the table.
        Definitions = Array.AsReadOnly(definitions);
    }

    /// <summary>
    /// Gets the definitions in this table, in the order they were mapped.
    /// </summary>
    public IReadOnlyList<RouteDefinition> Definitions { get; }

    /// <summary>
    /// Matches a URI to a route.
    /// </summary>
    /// <param name="uri">
    /// The URI to match. Only its path is read; see the remarks for which part of an absolute or a relative URI that
    /// is.
    /// </param>
    /// <returns>
    /// A successful result with the route and its definition when a template matches the path, or a failed result
    /// with at least one diagnostic when none does; in this version that is one
    /// <see cref="RouteDiagnosticCodes.NotMatched"/> diagnostic.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For an absolute URI, the path is <see cref="Uri.AbsolutePath"/>; the scheme, user information, host, port,
    /// query and fragment are ignored. A link with a custom scheme and two slashes, such as
    /// <c>myapp://settings/profile</c>, has the host <c>settings</c> and the path <c>/profile</c>, so its first word
    /// is ignored with the host. Write such a link with three slashes, as <c>myapp:///settings/profile</c>, which has
    /// no host and the path <c>/settings/profile</c>.
    /// </para>
    /// <para>
    /// The path of an absolute URI is the one <see cref="Uri"/> produces: it decodes percent-encoded unreserved
    /// characters, percent-encodes spaces and non-ASCII characters, and keeps repeated slashes. In a URI with an
    /// authority, even an empty one as in <c>myapp:///a/../b</c>, it also removes dot segments; in one with no
    /// authority at all, such as <c>app:/a/../b</c>, it keeps them, so that path does not match. In a URI that
    /// contains non-ASCII text anywhere, raw or percent-encoded, it also drops the encoded spaces at the very end:
    /// the path of <c>https://host/%C3%A9%20</c> is <c>/%C3%A9</c>. The table does not work around this.
    /// </para>
    /// <para>
    /// For a relative URI, the path is <see cref="Uri.OriginalString"/> up to the first <c>?</c> or <c>#</c>, whichever
    /// comes first, with no other change: nothing is trimmed, and dot segments are not resolved, so a path with one
    /// does not match. Build app-relative links with <see cref="UriKind.Relative"/> or
    /// <see cref="UriKind.RelativeOrAbsolute"/>: <see cref="Uri(string)"/> makes a string that starts with <c>/</c> a
    /// file URI on Unix-like platforms and throws for it on Windows. The table reads such a file URI as relative.
    /// </para>
    /// <para>
    /// The path is matched as <see cref="RouteTemplate.Match(string)"/> matches it. In this version, definitions are
    /// tried in the order they were mapped, and the first whose template matches wins; no later one is tried. Its
    /// create function is called once, and any exception it throws propagates unchanged.
    /// </para>
    /// <para>
    /// A URI that no template matches is not an error. The table itself does not throw for the content of a URI; an
    /// exception that a create function throws propagates to the caller.
    /// </para>
    /// <para>
    /// The table checks neither the scheme nor the host, so <c>https://other.example/account</c> and
    /// <c>file:///account</c> match exactly as the application's own links do. Deciding which sources to accept is
    /// the job of the code that receives the link.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The create function of the definition that matched returned <see langword="null"/>.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that the create function of the definition that matched throws, unchanged.
    /// </exception>
    public RouteMatchResult Match(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string path = ReadPath(uri);

        // The path is split once and each segment decoded at most once for all the definitions, so that allocation and
        // decoding grow with the path plus the definitions tried rather than with their product.
        string[] pathSegments = RouteText.SplitSegments(path);
        var decodedCount = 0;
        foreach (RouteDefinition definition in _matchOrder)
        {
            IReadOnlyDictionary<string, string>? values = definition.Template.Match(pathSegments, ref decodedCount);
            if (values is null)
                continue;

            // The create function is not guarded: the first matching definition decides the route, so an exception
            // is the application's to see, and falling through to a later definition would hide it.
            AppRoute route = definition.CreateRoute(new RouteMatchContext(uri, values)) ??
                throw new InvalidOperationException(
                    $"The create function for route type '{definition.RouteType}', mapped to the template " +
                    $"'{definition.Template.Value}', returned null; it must return a route.");

            return RouteMatchResult.Success(route, definition);
        }

        // A deep-link path can carry a secret, such as a sign-in token, and messages tend to be logged, so the path is
        // only in Data, exactly as it was matched, where logging it is the caller's decision.
        return RouteMatchResult.Failure(new RouteDiagnostic(
            RouteDiagnosticCodes.NotMatched,
            "No route matches the path of the URI.",
            new Dictionary<string, object?>(1, StringComparer.Ordinal) { ["path"] = path }));
    }

    private static string ReadPath(Uri uri)
    {
        // A relative URI has no parsed parts: AbsolutePath throws for one, so its path is read from the text. On
        // Unix-like platforms, new Uri("/settings?tab=1") is an implicit file URI whose AbsolutePath escapes the '?'
        // into the path, so a file URI whose text starts with '/', where the caller wrote no file: scheme, is read
        // from its text too, as the app-relative link it was meant to be.
        if (uri.IsAbsoluteUri && !(uri.IsFile && uri.OriginalString.StartsWith('/')))
            return uri.AbsolutePath;

        // A fragment starts at the first '#' and a query at the first '?' before it, so the path ends at whichever
        // comes first.
        string text = uri.OriginalString;
        int end = text.AsSpan().IndexOfAny('?', '#');
        return end < 0 ? text : text[..end];
    }
}
