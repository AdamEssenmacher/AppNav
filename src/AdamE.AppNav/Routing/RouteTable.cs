using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace AdamE.AppNav.Routing;

/// <summary>
/// Matches URIs to typed routes, using the templates mapped by a <see cref="RouteTableBuilder"/>.
/// </summary>
/// <remarks>
/// A table is immutable once built, and safe to use from several threads at once: <see cref="Match(Uri)"/>,
/// <see cref="Format(AppRoute)"/> and <see cref="FormatUri(AppRoute, Uri)"/> read nothing that can change. A create
/// function, or a value function of a format function, that is called from several threads must itself be safe to call
/// that way.
/// </remarks>
public sealed class RouteTable
{
    // The definitions in the order they are tried, by precedence, sorted once here so that Match only walks it.
    private readonly RouteDefinition[] _matchOrder;

    // The codecs registered on the builder, each under the type it reads, handed to every context.
    private readonly FrozenDictionary<Type, object> _codecs;

    // The formatter of each route type whose mapping has a format function, bound to the codecs above, with the
    // position of that mapping in _matchOrder, keyed by the exact route type, which Format looks a route up by. Only
    // the definitions before that position can match a written path first, so Format checks those alone.
    private readonly FrozenDictionary<Type, (RouteFormatter Formatter, int Position)> _formatters;

    // Every route type that some mapping creates, so that Format can tell a type that is not mapped at all from one
    // that is mapped without a format function, and say which fix applies.
    private readonly FrozenSet<Type> _routeTypes;

    internal RouteTable(RouteDefinition[] definitions, FrozenDictionary<Type, object> codecs)
    {
        ThrowIfSameTemplateTwice(definitions);
        ThrowIfAmbiguous(definitions);

        // Templates that rank equally at every position cannot both match a path, as checked above, so their order
        // among themselves never decides which route wins. Array.Sort is not stable, so it sorts registration indices
        // and breaks ties on them only so that the order is the same on every build.
        var order = new int[definitions.Length];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(order, (x, y) =>
        {
            int difference = definitions[x].Template.ComparePrecedence(definitions[y].Template);
            return difference != 0 ? difference : x.CompareTo(y);
        });

        _matchOrder = new RouteDefinition[order.Length];
        var positions = new int[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            _matchOrder[i] = definitions[order[i]];
            positions[order[i]] = i;
        }

        // A read-only wrapper, so that a caller cannot cast Definitions back to the array and change the table.
        Definitions = Array.AsReadOnly(definitions);
        _codecs = codecs;
        _formatters = BindFormatters(definitions, positions, codecs);

        var routeTypes = new HashSet<Type>(definitions.Length);
        foreach (RouteDefinition definition in definitions)
            routeTypes.Add(definition.RouteType);

        _routeTypes = routeTypes.ToFrozenSet();
    }

    // Read by tests only, which check the order in which templates are tried.
    internal ReadOnlySpan<RouteDefinition> MatchOrder => _matchOrder;

    /// <summary>
    /// Gets the definitions in this table, in the order they were mapped. This is not the order in which
    /// <see cref="Match(Uri)"/> tries them, which follows precedence.
    /// </summary>
    public IReadOnlyList<RouteDefinition> Definitions { get; }

    /// <summary>
    /// Matches a URI to a route.
    /// </summary>
    /// <param name="uri">
    /// The URI to match. Only its path decides the route; see the remarks for which part of an absolute or a relative
    /// URI that is, and for the query, which a create function can read.
    /// </param>
    /// <returns>
    /// A successful result with the route and its definition when a template matches the path. Otherwise a failed
    /// result with at least one diagnostic; in this version that is one <see cref="RouteDiagnosticCodes.NotMatched"/>
    /// diagnostic when no template matches, or one <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, with
    /// the definition that matched, when its create function throws a <see cref="RouteValueException"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For an absolute URI, the path is <see cref="Uri.AbsolutePath"/>; the scheme, user information, host, port,
    /// query and fragment do not affect which route matches. A link with a custom scheme and two slashes, such as
    /// <c>myapp://settings/profile</c>, has the host <c>settings</c> and the path <c>/profile</c>, so its first word
    /// is ignored with the host. Write such a link with three slashes, as <c>myapp:///settings/profile</c>, which has
    /// no host and the path <c>/settings/profile</c>.
    /// </para>
    /// <para>
    /// The path of an absolute URI is the one <see cref="Uri"/> produces: it decodes percent-encoded unreserved
    /// characters, percent-encodes spaces and non-ASCII characters, and keeps repeated slashes. In a URI with an
    /// authority, even an empty one as in <c>myapp:///a/../b</c>, it also removes dot segments; in one with no
    /// authority at all, such as <c>app:/a/../b</c>, it keeps them, so that path does not match.
    /// </para>
    /// <para>
    /// <see cref="Uri"/> drops <c>%20</c> sequences at the very end of a URI that contains, anywhere including the
    /// host, a non-ASCII character, a percent-encoded byte from <c>%80</c> to <c>%FF</c>, or a percent-encoded
    /// unreserved character such as <c>%41</c>: the path of <c>https://example.com/%C3%A9%20</c> is <c>/%C3%A9</c>,
    /// and the query of <c>https://example.com/%41?v=%20</c> is <c>?v=</c>. Any character after them, even a lone
    /// <c>#</c> or <c>&amp;</c>, keeps them. The table does not work around this.
    /// </para>
    /// <para>
    /// For a relative URI, the path is <see cref="Uri.OriginalString"/> up to the first <c>?</c> or <c>#</c>, whichever
    /// comes first, with no other change: nothing is trimmed, and dot segments are not resolved, so a path with one
    /// does not match. Build app-relative links with <see cref="UriKind.Relative"/> or
    /// <see cref="UriKind.RelativeOrAbsolute"/>: <see cref="Uri(string)"/> makes a string that starts with <c>/</c> a
    /// file URI on Unix-like platforms and throws for it on Windows. The table reads such a file URI as relative when
    /// its text starts with <c>/</c>; with leading white space, as in <c> /a?x=1</c>, it is read as absolute, with the
    /// <c>?</c> in its path and no query values.
    /// </para>
    /// <para>
    /// The query never affects which route matches. A create function reads it through
    /// <see cref="RouteMatchContext.TryGetQuery(string, out string)"/> and
    /// <see cref="RouteMatchContext.GetQueryValues(string)"/>: for an absolute URI it is <see cref="Uri.Query"/>, as
    /// <see cref="Uri"/> reports it, and for a relative one, or a file URI read as relative, the original text from
    /// the first <c>?</c> that comes before any <c>#</c>, up to the next <c>#</c>. Some schemes, such as <c>ftp</c>,
    /// have no query in <see cref="Uri"/>, so a <c>?</c> in them is part of the path. The query is read and parsed
    /// only when a create function first asks for a value, at most once per match, so a URI that matches nothing, and
    /// a route that reads no query values, pay nothing for it. It never appears in a diagnostic.
    /// </para>
    /// <para>
    /// The path is matched as <see cref="RouteTemplate.Match(string)"/> matches it. Definitions are tried by
    /// precedence, not in the order they were mapped: two templates are compared segment by segment from the left,
    /// and at the first position where their ranks differ, the more specific one is tried first, as the "Which route
    /// wins" section of the routing guide sets out. <see cref="RouteTableBuilder.Build"/> rejects templates that rank
    /// equally at every position and can match the same path, so the order the routes were mapped in never decides
    /// which one wins. The first whose template matches wins; no later one is tried. Its create function is called
    /// once, and any exception it throws propagates unchanged, except a <see cref="RouteValueException"/>.
    /// </para>
    /// <para>
    /// A create function that finds a value unusable throws a <see cref="RouteValueException"/>, and the match fails
    /// with a <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic instead of throwing. No later template is
    /// tried, even one that also matches the path: the template that matched owns the URI. A constraint, by contrast,
    /// makes the template not match, so that a later one can. Only an exception thrown by the call to the create
    /// function of the definition that matched is turned into a result.
    /// </para>
    /// <para>
    /// A URI that no template matches is not an error. The table itself does not throw for the content of a URI; an
    /// exception other than <see cref="RouteValueException"/> that a create function throws propagates to the caller,
    /// and so does any exception, a <see cref="RouteValueException"/> included, that the function of a constraint
    /// added with <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, since it runs
    /// while a template is chosen, before any create function is called.
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
    /// Any exception other than <see cref="RouteValueException"/> that the create function of the definition that
    /// matched throws, unchanged.
    /// <para>-or-</para>
    /// Any exception, <see cref="RouteValueException"/> included, that the function of a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, unchanged.
    /// </exception>
    // A rejected value is thrown in the application's create function and caught here, so a debugger that breaks on
    // user-unhandled exceptions would stop on every rejected link. The attribute stops that for this method, and for
    // every other exception that passes through it too, so the call to the create function hands each of those back
    // to the debugger itself.
    [DebuggerDisableUserUnhandledExceptions]
    public RouteMatchResult Match(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string path = UriReader.ReadPath(uri, out int queryStart);

        // The path is split once and each segment decoded at most once for all the definitions, so that allocation and
        // decoding grow with the path plus the definitions tried rather than with their product.
        string[] pathSegments = RouteText.SplitSegments(path);
        var decodedCount = 0;
        int position = FindFirstMatch(
            pathSegments,
            ref decodedCount,
            _matchOrder.Length,
            capture: true,
            out IReadOnlyDictionary<string, string>? values);
        if (position < 0)
        {
            // A deep-link path can carry a secret, such as a sign-in token, and messages tend to be logged, so the path
            // is only in Data, exactly as it was matched, where logging it is the caller's decision.
            return RouteMatchResult.Failure(new RouteDiagnostic(
                RouteDiagnosticCodes.NotMatched,
                "No route matches the path of the URI.",
                new Dictionary<string, object?>(1, StringComparer.Ordinal) { ["path"] = path }));
        }

        // The first matching definition decides the route, so its create function's exceptions are the application's
        // to see, and falling through to a later definition would hide them. Only the one the create function throws
        // to reject a value is caught, by its exact type, around this call alone, so that one from anywhere else, and
        // every other exception, passes through untouched.
        RouteDefinition definition = _matchOrder[position];
        AppRoute? route;
        try
        {
            route = definition.CreateRoute(new RouteMatchContext(uri, values!, queryStart, _codecs));
        }
        catch (RouteValueException exception)
        {
            return ValueInvalid(definition, path, exception);
        }
        catch (Exception exception) when (BreakInDebugger(exception))
        {
            // Never entered: the filter only tells a debugger about the exception and declines it, so it goes on to
            // the caller exactly as it was thrown.
            throw;
        }

        return RouteMatchResult.Success(
            route ?? throw new InvalidOperationException(
                $"The create function for route type '{RouteValueTypes.DisplayName(definition.RouteType)}', " +
                $"mapped to the template '{definition.Template.Value}', returned null; it must return a route."),
            definition);
    }

    /// <summary>
    /// Writes a route as a relative URI of the template it is mapped to, by the format function its mapping was given.
    /// </summary>
    /// <param name="route">The route to write.</param>
    /// <returns>
    /// The route as the text of a relative URI: its path, as
    /// <see cref="RouteTemplate.Format(IReadOnlyDictionary{string, string})"/> writes it for the path values the
    /// format function reads from the route, followed, when at least one query value is written, by <c>?</c> and the
    /// query, such as <c>/orders/42</c> or <c>/products?page=2&amp;sort=name</c>. Each value is written as the
    /// canonical text of its type that
    /// <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/> describes. The text never
    /// has a fragment.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The mapping is found by the exact type of the route, as <see cref="object.GetType()"/> gives it: a mapping of a
    /// base type of the route does not write it. Each declared value function is called at most once: the path
    /// functions in template order, then the query functions in the order they were declared. After a value that
    /// cannot be written, or an exception from a value function, the later functions are not called, so no query
    /// function is called for a route whose path cannot be written. A <see langword="null"/> value leaves out an
    /// optional or catch-all parameter, or a query value, and a <see langword="null"/> or empty list of query values
    /// writes nothing.
    /// </para>
    /// <para>
    /// The query values are written in the order they were declared, as <c>name=value</c> pairs joined by
    /// <c>&amp;</c>, the elements of a list of values in the order the list gives them, where the list was declared,
    /// and the <c>?</c> only when there is at least one pair. An empty string is written as <c>name=</c>,
    /// which reads back as the empty string. Names and values are percent-encoded with
    /// <see cref="Uri.EscapeDataString(string)"/>, so a space is written as <c>%20</c>, never <c>+</c>, which the query
    /// reads take for a space, and a <c>+</c> as <c>%2B</c>; <c>&amp;</c>, <c>=</c>, <c>#</c>, <c>%</c> and every
    /// character but ASCII letters, digits, <c>-</c>, <c>.</c>, <c>_</c> and <c>~</c> are encoded too.
    /// </para>
    /// <para>
    /// When this method returns, <see cref="Match(Uri)"/> of the text selects the mapping that wrote it, and each value
    /// reads back equal through the typed read of its type: a query value through the read of its first value, which is
    /// the only one written, and a list of query values through
    /// <see cref="RouteMatchContext.GetQueryValues{T}(string)"/>, which gives equal elements in the same order, and an
    /// empty list for a list that was <see langword="null"/>. So the route that matches is equal to the one written
    /// provided the create function and the format function agree, which the table cannot check, and the route type
    /// compares any list it holds by its elements, which a record does not do by default. A path that a template
    /// ranking higher would match first is not returned: with <c>/products/new</c> and <c>/products/{name}</c> in one
    /// table, the name <c>new</c> cannot be written, because <c>/products/new</c> would open the other route. Which
    /// template a path selects is decided exactly as <see cref="Match(Uri)"/> decides it, by the templates alone, so a
    /// query value never changes it, and no create function is called. That check comes after every other, once every
    /// value function has been called, and calls none. A mapping of the same route type that ranks higher also counts,
    /// because the table cannot know that its create function makes the same route.
    /// </para>
    /// <para>
    /// The promise is about <see cref="Match(Uri)"/> of the returned text as a relative URI, as in
    /// <c>new Uri(text, UriKind.Relative)</c>, and of an absolute URI with that path and query. <see cref="Uri"/>
    /// changes the text of an absolute URI in one way: it drops <c>%20</c> sequences at the very end of an absolute
    /// URI that contains a non-ASCII character anywhere, including in the host the application puts in front of the
    /// text, as the remarks on <see cref="Match(Uri)"/> describe. This method writes a non-ASCII character as
    /// percent-encoded bytes from <c>%80</c> to <c>%FF</c>, which count too. So the last value written, the last query
    /// value or the last path value when no query is written, loses its trailing spaces in such a link, and a path
    /// value that is only spaces then no longer matches. An absolute URI that is all ASCII, and any relative URI, keep
    /// them. With <c>/&#233;/new</c> and <c>/&#233;/{name}</c>, the name <c>new </c> is written as
    /// <c>/%C3%A9/new%20</c>, which matches as that name as a relative URI, while <c>https://app/%C3%A9/new%20</c>
    /// opens the route of <c>/&#233;/new</c>. To write an absolute link, use <see cref="FormatUri(AppRoute, Uri)"/>,
    /// which has no such limit: it checks the link it builds, and throws for one that would be read back differently
    /// rather than return it.
    /// </para>
    /// <para>
    /// The table is not changed, and several threads can call this method and <see cref="Match(Uri)"/> at once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No mapping of exactly the type of <paramref name="route"/> has a format function. The message names the type
    /// and says whether the type is not mapped at all, or is mapped but without a format function.
    /// <para>-or-</para>
    /// The parser of a codec registered with <see cref="RouteTableBuilder.AddValueCodec{TValue}"/>, reading back a
    /// text its writer wrote, accepted it but gave <see langword="null"/>. The message names the type, but neither the
    /// value nor the text.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A value of the route cannot be written: a required parameter's value is <see langword="null"/>; a value is
    /// empty, contains a lone surrogate, does not satisfy the parameter's constraint or is a dot segment, <c>.</c> or
    /// <c>..</c>; a catch-all value starts or ends with <c>/</c>, contains <c>//</c> or has a dot segment as a part; an
    /// optional parameter has a value although an earlier one is left out; a query value contains a lone surrogate; an
    /// element of a list of query values is <see langword="null"/>; a value has no text, such as a
    /// <see cref="double"/> that is NaN, an enum value that is not the value of a member, or a value whose codec,
    /// registered with <see cref="RouteTableBuilder.AddValueCodec{TValue}"/>, gives <see langword="null"/> for it; or
    /// such a codec writes a text that its own parser does not accept. The exception's
    /// <see cref="ArgumentException.ParamName"/> is <c>route</c>, and its message names the route type, the template
    /// and the path parameter or query value and says what is wrong, but never quotes the value, or the text a codec
    /// wrote for it, either of which can be a secret, such as a sign-in token. It has no inner exception. When more
    /// than one value cannot be written, which one is reported is not specified.
    /// <para>-or-</para>
    /// The path written for the route is matched first by the template of another mapping, one that ranks higher, so
    /// <see cref="Match(Uri)"/> of it would select that mapping instead, whether or not that mapping has a format
    /// function. The exception's <see cref="ArgumentException.ParamName"/> is <c>route</c>, and its message names the
    /// route type and both templates, but neither the route's values nor the path. It has no inner exception. To fix
    /// the table, change the templates so that they cannot match the same path, for example by constraining a
    /// parameter or by giving one of them a literal of its own.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that a value function throws, that a list of query values throws while it is enumerated, that
    /// a codec registered with <see cref="RouteTableBuilder.AddValueCodec{TValue}"/> throws while it writes a value or
    /// reads back the text it wrote, or that the function of a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, unchanged.
    /// </exception>
    public string Format(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return Write(route, out _);
    }

    /// <summary>
    /// Writes a route as an absolute URI, behind the scheme and authority of a base URI, by the format function its
    /// mapping was given.
    /// </summary>
    /// <param name="route">The route to write.</param>
    /// <param name="baseUri">
    /// The absolute URI whose scheme, user information, host and port the link is written behind, such as
    /// <c>https://example.com</c> or <c>myapp:///</c>. Its path must be empty or <c>/</c>, and it must have no query
    /// and no fragment.
    /// </param>
    /// <returns>
    /// An absolute URI of the scheme, user information, host and port of <paramref name="baseUri"/>, followed by
    /// exactly the text that <see cref="Format(AppRoute)"/> returns for the route, such as
    /// <c>https://example.com/orders/42</c> or <c>myapp:///orders/42</c>, which <see cref="Match(Uri)"/> reads back
    /// as that path and query. Pass the <see cref="Uri"/> itself, or its <see cref="Uri.AbsoluteUri"/>, to other
    /// code; never its <see cref="Uri.ToString()"/> or string interpolation of it, which unescape characters such as
    /// <c>%20</c> and can change the link when it is parsed again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The route is written once, exactly as <see cref="Format(AppRoute)"/> writes it: each value function is called
    /// at most once, in the same order, and each failure of <see cref="Format(AppRoute)"/> is thrown unchanged. The
    /// base is checked first, so a base that cannot be used is reported before any value function is called.
    /// </para>
    /// <para>
    /// The base gives only the scheme and the authority, because <see cref="Match(Uri)"/> reads the whole path of a
    /// link: written under <c>https://example.com/app</c>, the route of <c>/orders/{id:int}</c> would have the path
    /// <c>/app/orders/42</c>, which its template does not match. Put a prefix that every link shares in the templates
    /// instead. The scheme, user information, host and port are written as <see cref="Uri"/> writes them in the
    /// <see cref="Uri.AbsoluteUri"/> of the base, with the scheme and a host name in lower case and a default port
    /// left out. User information in the base is copied into every link written from it, so a credential in the base
    /// travels with each link; no exception message includes it. A base with an empty authority, <c>myapp:///</c>,
    /// gives a link with no host, <c>myapp:///orders/42</c>; one with a host, <c>myapp://host</c>, gives
    /// <c>myapp://host/orders/42</c>, whose host <see cref="Match(Uri)"/> ignores; and one with no authority,
    /// <c>myapp:</c> or <c>myapp:/</c>, gives <c>myapp:/orders/42</c>.
    /// </para>
    /// <para>
    /// A base whose path is empty or <c>/</c> is accepted under any scheme whose links read back as written:
    /// <c>mailto:a@b.c</c>, which <see cref="Uri"/> reads as user information and a host with an empty path, gives
    /// <c>mailto:a@b.c/orders/42</c>, while <c>urn:isbn:1</c> and <c>about:blank</c> have a path and are refused. This
    /// method promises that <see cref="Match(Uri)"/> reads the link back, not that the link is useful under its scheme.
    /// </para>
    /// <para>
    /// When this method returns, the path and the query that <see cref="Match(Uri)"/> reads from the result are
    /// exactly the text that was written. So everything <see cref="Format(AppRoute)"/> promises for that text as a
    /// relative URI holds for the result: <see cref="Match(Uri)"/> of it selects the mapping that wrote it, and each
    /// value reads back equal through the typed read of its type. Where <see cref="Uri"/> would read the link back
    /// differently, this method throws rather than return it. That happens when the last value written ends with a
    /// space and the URI contains a non-ASCII character, as the remarks on <see cref="Match(Uri)"/> describe, whether
    /// the character is in a value, a literal, the user information or the host of the base. It also happens under a
    /// scheme for which <see cref="Uri"/> changes the path or the query: <c>ftp</c> and the other schemes that have no
    /// query in <see cref="Uri"/> refuse a link with a query; <c>file</c> and <c>vsmacros</c> refuse a value that
    /// contains <c>/</c> in a segment that is not a catch-all, which they decode from <c>%2F</c>, while a catch-all
    /// value's <c>/</c> is written as it is and is unaffected; and <c>net.tcp</c> and <c>net.pipe</c> refuse a path
    /// value that contains <c>\</c>, which they read as <c>/</c>. The behaviour of <c>file</c> was established on
    /// Unix-like platforms and can differ on Windows, where <see cref="Uri"/> treats file URIs differently; the check
    /// runs on every platform, so the promise holds everywhere, even where which links are refused differs. No
    /// shorter or changed link is ever returned in place of one that is refused.
    /// </para>
    /// <para>
    /// This method checks the read-back with one <see cref="Uri"/>, the result, and calls no create function. The
    /// table is not changed, and several threads can call this method, <see cref="Format(AppRoute)"/> and
    /// <see cref="Match(Uri)"/> at once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="route"/> or <paramref name="baseUri"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No mapping of exactly the type of <paramref name="route"/> has a format function, or the parser of a codec gave
    /// <see langword="null"/> for a text its writer wrote, as for <see cref="Format(AppRoute)"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="baseUri"/> is a relative URI, or a path such as <c>/</c> that the table reads as relative; or it
    /// has a path other than <c>/</c>, a query or a fragment, even an empty one, as in <c>https://example.com?</c>; or
    /// its host is an IPv6 address with a zone id, as in <c>http://[fe80::1%25en0]</c>, which <see cref="Uri"/>
    /// leaves out of the text of a URI. The exception's <see cref="ArgumentException.ParamName"/> is <c>baseUri</c>,
    /// and its message says what is wrong but never quotes the base, which can carry user information.
    /// <para>-or-</para>
    /// <see cref="Uri"/> does not read back the path and query written under the scheme of <paramref name="baseUri"/>.
    /// The exception's <see cref="ArgumentException.ParamName"/> is <c>baseUri</c>, and its message names the scheme
    /// alone.
    /// <para>-or-</para>
    /// <see cref="Uri"/> cannot parse the link written behind <paramref name="baseUri"/>, which no base or route is
    /// known to cause. The exception's <see cref="ArgumentException.ParamName"/> is <c>baseUri</c>.
    /// <para>-or-</para>
    /// The last value written ends with a space, and the URI contains a non-ASCII character, so <see cref="Uri"/>
    /// drops the space. The exception's <see cref="ArgumentException.ParamName"/> is <c>route</c>, and its message
    /// names the route type and the template, but neither the value, the text nor the base.
    /// <para>-or-</para>
    /// A value of the route cannot be written, or the path written for it is matched first by the template of
    /// another mapping, as for <see cref="Format(AppRoute)"/>, with the same
    /// <see cref="ArgumentException.ParamName"/> and message.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that a value function throws, that a list of query values throws while it is enumerated, that
    /// a codec registered with <see cref="RouteTableBuilder.AddValueCodec{TValue}"/> throws while it writes a value or
    /// reads back the text it wrote, or that the function of a constraint added with
    /// <see cref="RouteTableBuilder.AddConstraint(string, Func{string, bool})"/> throws, unchanged.
    /// </exception>
    public Uri FormatUri(AppRoute route, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(baseUri);

        string authority = ReadAuthority(baseUri);
        string text = Write(route, out RouteDefinition owner);
        // No base that passes the checks above, and no text Format writes, is known to give a link that Uri cannot
        // parse. Should one, it is refused in its own words, so that a UriFormatException never escapes and the
        // message does not blame a scheme that may be fine.
        if (!Uri.TryCreate(authority + text, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException(
                $"The route type '{RouteValueTypes.DisplayName(route.GetType())}' cannot be written by the template " +
                $"'{owner.Template.Value}' as an absolute URI: System.Uri cannot parse the link written behind the " +
                "base URI. Use the relative text that Format returns.",
                nameof(baseUri));
        }

        // Read as Match reads it, so that the check holds for what Match will see, and compared in place, so that a
        // link read back unchanged costs no more strings than reading it does.
        string path = UriReader.ReadPath(uri, out int queryStart);
        ReadOnlySpan<char> query = UriReader.ReadQuery(uri, queryStart);
        ReadOnlySpan<char> written = text;
        int read = path.Length + query.Length;
        bool readFromStart =
            read <= written.Length && written.StartsWith(path) && written[path.Length..].StartsWith(query);
        if (readFromStart && read == written.Length)
            return uri;

        // The message never quotes the value, the text or the base, any of which can carry a secret.
        if (readFromStart && IsEncodedSpaces(written[read..]))
        {
            throw new ArgumentException(
                $"The route type '{RouteValueTypes.DisplayName(route.GetType())}' cannot be written by the template " +
                $"'{owner.Template.Value}' as an absolute URI: the last value written ends with a space, which " +
                "System.Uri drops from the end of an absolute URI that contains a non-ASCII character, so the link " +
                "would be read back differently. Write a value that does not end with a space, or use the relative " +
                "text that Format returns. A base whose host and user information are ASCII avoids this only when " +
                "nothing else in the link is non-ASCII.",
                nameof(route));
        }

        throw SchemeChangesLink(baseUri);
    }

    // Names the scheme alone: the rest of the base can carry user information.
    private static ArgumentException SchemeChangesLink(Uri baseUri)
    {
        return new ArgumentException(
            $"A URI of the scheme '{baseUri.Scheme}' does not keep the link as written: System.Uri changes its path " +
            "or query, so the link would be read back differently. Use a scheme whose URIs keep them, such as https " +
            "or a custom scheme of the application.",
            nameof(baseUri));
    }

    // Writes the route as Format describes, and gives the definition that wrote it, for a message about the text.
    private string Write(AppRoute route, out RouteDefinition owner)
    {
        Type type = route.GetType();
        if (!_formatters.TryGetValue(type, out (RouteFormatter Formatter, int Position) writer))
        {
            throw new InvalidOperationException(_routeTypes.Contains(type)
                ? $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written, because it is " +
                  "mapped, but none of its mappings has a format function. Pass one to the RouteTableBuilder.Map " +
                  "overload that takes it; for a template with no parameters, a function that declares nothing, " +
                  "as in '_ => { }'."
                : $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written, because no " +
                  "mapping of exactly that type exists. A mapping of a base type does not write a route of a type " +
                  "derived from it; map the type itself with a format function.");
        }

        // Every value function has been called, and every value that cannot be written reported, before the path is
        // checked here, so that this check reports a table that needs fixing only for a route that is otherwise valid.
        // A query value never changes which template matches, so only the path is checked.
        string text = writer.Formatter.Format(route, out string path);

        // A definition ranked first has none above it that could match the path first, so it pays nothing more.
        owner = _matchOrder[writer.Position];
        RouteDefinition? first = writer.Position == 0 ? null : FindMatchedFirst(path, writer.Position);
        if (first is null)
            return text;

        // The message names both templates, but neither the route's values nor the path, which can carry a secret.
        // The capturing template can contain a literal equal to a value, as '/products/new' does for the name 'new',
        // but such a value is text the application wrote into its own template, not a secret. It says which template
        // matches, not which route opens, because that template's create function may still reject the value.
        throw new ArgumentException(
            $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written by the template " +
            $"'{owner.Template.Value}': the path it writes is matched first by the template " +
            $"'{first.Template.Value}', which ranks higher, so a link to it would be matched by that template " +
            "instead. Change the templates so that they cannot match the same path, for example by constraining a " +
            "parameter or by giving one of them a literal of its own.",
            nameof(route));
    }

    // Returns the scheme, user information, host and port of a base, as the start of an absolute URI that a link's
    // path follows, or throws when the base is not one that a link can be written behind. The base's path, query and
    // fragment are refused rather than dropped or kept: Match reads the whole path, so a link under a base path would
    // not match, and a link written after a query or a fragment would be read as part of it. An empty query or
    // fragment counts, since Uri reports it as "?" or "#".
    private static string ReadAuthority(Uri baseUri)
    {
        if (UriReader.IsReadAsRelative(baseUri))
        {
            throw new ArgumentException(
                "The base URI must be an absolute URI with a scheme, such as 'https://example.com' or 'myapp:///'; a " +
                "relative URI, or a path such as '/' that System.Uri makes a file URI, gives no scheme to write the " +
                "link behind.",
                nameof(baseUri));
        }

        string? part = baseUri.AbsolutePath is not ("" or "/") ? "path"
            : baseUri.Query.Length > 0 ? "query"
            : baseUri.Fragment.Length > 0 ? "fragment"
            : null;
        if (part is not null)
        {
            throw new ArgumentException(
                $"The base URI has a {part}, but a base gives only the scheme, user information, host and port of a " +
                "link. Match reads the whole path of a link, so a link written under a base path would not match " +
                "this table. Put a path that every link shares in the templates instead.",
                nameof(baseUri));
        }

        // Uri leaves the zone id of an IPv6 host out of AbsoluteUri and of every component it writes, and keeps it
        // only in IdnHost and DnsSafeHost, so a link built from the base would silently name another address.
        if (baseUri.HostNameType == UriHostNameType.IPv6 && baseUri.IdnHost.Contains('%'))
        {
            throw new ArgumentException(
                "The base URI's host is an IPv6 address with a zone id, which System.Uri leaves out of the text of a " +
                "URI, so the link would name a different address.",
                nameof(baseUri));
        }

        return baseUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.UserInfo, UriFormat.UriEscaped);
    }

    // Whether the text is one or more "%20", as Format writes a space and as Uri drops it from the end of a URI.
    private static bool IsEncodedSpaces(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text.Length % 3 != 0)
            return false;

        for (var i = 0; i < text.Length; i += 3)
        {
            if (!text.Slice(i, 3).SequenceEqual("%20"))
                return false;
        }

        return true;
    }

    // Returns the definition that Match would select for a path that the definition at position wrote, when it is one
    // ranked above that definition, or null when none is. The path is split as Match splits the path of a relative
    // URI, which is the written text itself, because a written path has no '?' or '#' that is not percent-encoded.
    private RouteDefinition? FindMatchedFirst(string path, int position)
    {
        string[] pathSegments = RouteText.SplitSegments(path);
        var decodedCount = 0;
        int first = FindFirstMatch(pathSegments, ref decodedCount, position, capture: false, out _);
        if (first >= 0)
            return _matchOrder[first];

        Debug.Assert(
            _matchOrder[position].Template.Matches(pathSegments, ref decodedCount),
            "A formatter writes only paths that its own template matches.");
        return null;
    }

    // Returns the position in _matchOrder of the first definition, before end, whose template matches the segments,
    // or -1 when none does. Match and Format both select here, so that Format refuses exactly the paths that Match
    // would give to another definition. Only Match needs the captured values: without capture, a template that
    // matches builds no dictionary, so Format's walk allocates nothing beyond the decoded segments.
    private int FindFirstMatch(
        string[] pathSegments,
        ref int decodedCount,
        int end,
        bool capture,
        out IReadOnlyDictionary<string, string>? values)
    {
        for (var i = 0; i < end; i++)
        {
            RouteTemplate template = _matchOrder[i].Template;
            if (capture)
            {
                values = template.Match(pathSegments, ref decodedCount);
                if (values is not null)
                    return i;
            }
            else if (template.Matches(pathSegments, ref decodedCount))
            {
                values = null;
                return i;
            }
        }

        values = null;
        return -1;
    }

    // Keys each format function by its route type, which may have only one, and binds it to the table's codecs, which
    // reports a value of a type that has no codec. Each check runs in the order the routes were mapped, so the error
    // does not change from one build to the next.
    private static FrozenDictionary<Type, (RouteFormatter Formatter, int Position)> BindFormatters(
        RouteDefinition[] definitions,
        int[] positions,
        FrozenDictionary<Type, object> codecs)
    {
        // Every second format function is reported before anything is bound, so that it is reported even when a
        // format function also writes a type that has no codec: registering one would not make the table valid, and
        // fixing it would take two rounds.
        var owners = new Dictionary<Type, RouteDefinition>();
        foreach (RouteDefinition definition in definitions)
        {
            if (definition.Formatter is null)
                continue;

            if (owners.TryGetValue(definition.RouteType, out RouteDefinition? earlier))
            {
                throw new InvalidOperationException(
                    $"The route type '{RouteValueTypes.DisplayName(definition.RouteType)}' is mapped with a format " +
                    $"function to both '{earlier.Template.Value}' and '{definition.Template.Value}', so which " +
                    "template writes a route of that type would be ambiguous. Give a format function to only one of " +
                    "the mappings; the others can still create the route.");
            }

            owners.Add(definition.RouteType, definition);
        }

        var formatters = new Dictionary<Type, (RouteFormatter Formatter, int Position)>(owners.Count);
        for (var i = 0; i < definitions.Length; i++)
        {
            RouteFormatter? formatter = definitions[i].Formatter;
            if (formatter is not null)
                formatters.Add(definitions[i].RouteType, (formatter.Bind(codecs), positions[i]));
        }

        return formatters.ToFrozenDictionary();
    }

    // Like the path, the name and the reason can carry text from the URI, so they are only in Data. The exception
    // itself is not kept: its inner exception, and the message of that, may quote the value, so only the name and the
    // message the create function gave are carried.
    private static RouteMatchResult ValueInvalid(RouteDefinition definition, string path, RouteValueException exception)
    {
        return RouteMatchResult.Failure(
            new RouteDiagnostic(
                RouteDiagnosticCodes.ValueInvalid,
                "A value in the URI is not valid for the route that matched.",
                new Dictionary<string, object?>(4, StringComparer.Ordinal)
                {
                    ["name"] = exception.ValueName,
                    ["template"] = definition.Template.Value,
                    ["reason"] = exception.Message,
                    ["path"] = path,
                }),
            definition);
    }

    // Two definitions with the same template match the same paths and rank equally, so the later one could never win.
    // A hash of the templates finds the pair in one pass. The first definition, in the order the routes were mapped,
    // whose template repeats an earlier one is reported, with the definition it repeats, so the error does not change
    // from one build to the next.
    private static void ThrowIfSameTemplateTwice(RouteDefinition[] definitions)
    {
        var first = new Dictionary<RouteTemplate, RouteDefinition>(definitions.Length, SameTemplateComparer.Instance);
        foreach (RouteDefinition definition in definitions)
        {
            if (first.TryAdd(definition.Template, definition))
                continue;

            RouteDefinition earlier = first[definition.Template];
            (string earlierType, string laterType) =
                RouteValueTypes.DisplayNames(earlier.RouteType, definition.RouteType);
            throw new InvalidOperationException(
                $"The templates '{earlier.Template.Value}', mapped to route type '{earlierType}', and " +
                $"'{definition.Template.Value}', mapped to route type '{laterType}', are the same template, " +
                "so the second could never be matched. Templates are the same when they have the same segments, " +
                "ignoring parameter names. Remove one mapping or change one of the templates.");
        }
    }

    // Two templates that rank equally at every position and both match some path would be tried in registration order,
    // so that order would decide which route wins for that path. Only templates of the same shape can be such a pair,
    // so the definitions are grouped by shape and each is compared with the earlier members of its group only. The
    // members of a group are kept apart in pairs, or Build has already thrown, so a group holds at most one member for
    // each combination of mutually disjoint constraints at its constrained required positions: up to 3^k for k such
    // positions, one for each class of built-in constraints that overlap (the numbers, bool and alpha, and guid). An
    // added constraint is never disjoint from any, so it keeps no pair apart and does not raise that bound. The check
    // is linear for tables whose groups are small, which is every realistic table, and quadratic in the size of a
    // group.
    // previous chains each group from its latest member back to its first. Among the built-in constraints, at most one
    // earlier member of a group can conflict with a new one, because they overlap in classes; an added constraint
    // overlaps every constraint, so several can, and the earliest of them is the one reported, so that the error does
    // not change from one build to the next.
    private static void ThrowIfAmbiguous(RouteDefinition[] definitions)
    {
        var latest = new Dictionary<RouteTemplate, int>(definitions.Length, SameTemplateComparer.SameShape);
        var previous = new int[definitions.Length];
        for (var i = 0; i < definitions.Length; i++)
        {
            RouteTemplate template = definitions[i].Template;
            int earliest = -1;
            string? commonPath = null;
            int unknownPosition = -1;
            previous[i] = latest.TryGetValue(template, out int last) ? last : -1;
            for (int j = previous[i]; j >= 0; j = previous[j])
            {
                ConstraintOverlap overlap =
                    definitions[j].Template.FindCommonPath(template, out string? path, out int position);
                if (overlap != ConstraintOverlap.Disjoint)
                    (earliest, commonPath, unknownPosition) = (j, path, position);
            }

            if (earliest >= 0)
                throw Ambiguous(definitions[earliest], definitions[i], commonPath, unknownPosition);

            latest[template] = i;
        }
    }

    // With a path, both templates are known to match it. Without one, an added constraint at unknownPosition is the
    // reason the templates are not known to be apart, and no path is invented, since no text is known that its function
    // accepts together with the other constraint. The advice depends on the positions it is about: the required ones,
    // or, when only optional parameters tell the templates apart, those of the alternatives. Where an added constraint
    // is involved there, constraints are not offered as the way to keep templates apart, since an added constraint
    // keeps none apart, and the message says why. With built-in constraints alone, the message is as it always was.
    private static InvalidOperationException Ambiguous(
        RouteDefinition earlier,
        RouteDefinition later,
        string? path,
        int unknownPosition)
    {
        RouteTemplate template = earlier.Template;
        bool onlyOptional = template.HasSameRequiredSegments(later.Template);
        bool added = path is null || template.HasAddedConstraint(later.Template, required: !onlyOptional);

        (string earlierType, string laterType) = RouteValueTypes.DisplayNames(earlier.RouteType, later.RouteType);
        var message = new StringBuilder();
        message.Append($"The templates '{template.Value}', mapped to route type '{earlierType}', and ")
            .Append($"'{later.Template.Value}', mapped to route type '{laterType}', rank equally and ");
        if (path is not null)
        {
            message.Append($"both match the path '{path}', so which route wins would depend on the order they were ")
                .Append("mapped in. ");
        }
        else
        {
            (RouteConstraint constraint, RouteConstraint otherConstraint) =
                template.ConstraintsAt(later.Template, unknownPosition);
            int segment = unknownPosition + 1;
            message.Append("could both match a path, so which route wins would depend on the order they were mapped ")
                .Append("in: ")
                .Append(ReferenceEquals(constraint, otherConstraint)
                    ? $"both have the constraint '{constraint.Name}' at segment {segment}, which is not known to " +
                      "reject every text. "
                    : $"the constraints '{constraint.Name}' and '{otherConstraint.Name}' at segment {segment} are " +
                      "not known to accept no text in common. ");
        }

        if (added)
        {
            message.Append("A constraint added with AddConstraint is treated as overlapping every constraint, itself ")
                .Append("included, because which text its function accepts cannot be known. ");
        }

        if (onlyOptional)
        {
            message.Append(path is null
                    ? "Optional parameters never keep templates apart either, because a path that leaves them out " +
                      "matches both templates whenever it matches one. "
                    : "Optional parameters never keep templates apart, because both templates match the path " +
                      "without them. ")
                .Append(template.HasAddedConstraint(later.Template, required: false)
                    ? "Map the shorter path and each alternative as its own template, keeping the alternatives " +
                      "apart with a different literal, or remove one."
                    : "Map the shorter path and each alternative as its own template with a required parameter, " +
                      "giving the alternatives constraints that no text satisfies together, or remove one.");
        }
        else
        {
            message.Append(added
                ? "Keep them apart at a required position with a different literal, or make them rank differently, " +
                  "or remove one."
                : "Keep them apart at a required position, with a different literal or with constraints there that " +
                  "no text satisfies together, or make them rank differently, or remove one.");
        }

        return new InvalidOperationException(message.ToString());
    }

    // Tells an attached debugger that an exception the create function threw is not handled here, which the attribute
    // on Match would otherwise keep it from stopping on. The debugger takes that only from a method that has the
    // attribute, so this one has it too. It always declines the exception, and without a debugger it does nothing.
    [DebuggerDisableUserUnhandledExceptions]
    private static bool BreakInDebugger(Exception exception)
    {
        Debugger.BreakForUserUnhandledException(exception);
        return false;
    }
}
