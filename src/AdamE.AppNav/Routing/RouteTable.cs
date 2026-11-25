using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;

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
    // The definitions in the order they are tried, by precedence, sorted once here so that Match only walks it.
    private readonly RouteDefinition[] _matchOrder;

    // The codecs registered on the builder, each under the type it reads, handed to every context.
    private readonly FrozenDictionary<Type, object> _codecs;

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
        for (var i = 0; i < order.Length; i++)
            _matchOrder[i] = definitions[order[i]];

        // A read-only wrapper, so that a caller cannot cast Definitions back to the array and change the table.
        Definitions = Array.AsReadOnly(definitions);
        _codecs = codecs;
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
    /// exception other than <see cref="RouteValueException"/> that a create function throws propagates to the caller.
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
    /// </exception>
    // A rejected value is thrown in the application's create function and caught here, so a debugger that breaks on
    // user-unhandled exceptions would stop on every rejected link. The attribute stops that for this method, and for
    // every other exception that passes through it too, so the call to the create function hands each of those back
    // to the debugger itself.
    [DebuggerDisableUserUnhandledExceptions]
    public RouteMatchResult Match(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string path = ReadPath(uri, out int queryStart);

        // The path is split once and each segment decoded at most once for all the definitions, so that allocation and
        // decoding grow with the path plus the definitions tried rather than with their product.
        string[] pathSegments = RouteText.SplitSegments(path);
        var decodedCount = 0;
        foreach (RouteDefinition definition in _matchOrder)
        {
            IReadOnlyDictionary<string, string>? values = definition.Template.Match(pathSegments, ref decodedCount);
            if (values is null)
                continue;

            // The first matching definition decides the route, so its create function's exceptions are the
            // application's to see, and falling through to a later definition would hide them. Only the one the create
            // function throws to reject a value is caught, by its exact type, around this call alone, so that one from
            // anywhere else, and every other exception, passes through untouched.
            AppRoute? route;
            try
            {
                route = definition.CreateRoute(new RouteMatchContext(uri, values, queryStart, _codecs));
            }
            catch (RouteValueException exception)
            {
                return ValueInvalid(definition, path, exception);
            }
            catch (Exception exception) when (BreakInDebugger(exception))
            {
                // Never entered: the filter only tells a debugger about the exception and declines it, so it goes on
                // to the caller exactly as it was thrown.
                throw;
            }

            return RouteMatchResult.Success(
                route ?? throw new InvalidOperationException(
                    $"The create function for route type '{definition.RouteType}', mapped to the template " +
                    $"'{definition.Template.Value}', returned null; it must return a route."),
                definition);
        }

        // A deep-link path can carry a secret, such as a sign-in token, and messages tend to be logged, so the path is
        // only in Data, exactly as it was matched, where logging it is the caller's decision.
        return RouteMatchResult.Failure(new RouteDiagnostic(
            RouteDiagnosticCodes.NotMatched,
            "No route matches the path of the URI.",
            new Dictionary<string, object?>(1, StringComparer.Ordinal) { ["path"] = path }));
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
            throw new InvalidOperationException(
                $"The templates '{earlier.Template.Value}', mapped to route type '{earlier.RouteType}', and " +
                $"'{definition.Template.Value}', mapped to route type '{definition.RouteType}', are the same " +
                "template, so the second could never be matched. Templates are the same when they have the same " +
                "segments, ignoring parameter names. Remove one mapping or change one of the templates.");
        }
    }

    // Two templates that rank equally at every position and both match some path would be tried in registration order,
    // so that order would decide which route wins for that path. Only templates of the same shape can be such a pair,
    // so the definitions are grouped by shape and each is compared with the earlier members of its group only. The
    // members of a group are kept apart in pairs, or Build has already thrown, so a group holds at most one member for
    // each combination of mutually disjoint constraints at its constrained required positions: up to 3^k for k such
    // positions with the built-ins, one for each class of constraints that overlap (the numbers, bool and alpha, and
    // guid). The check is linear for tables whose groups are small, which is every realistic table, and quadratic in
    // the size of a group. previous chains each group from its latest member back to its first, and the earliest member
    // that conflicts is the one reported, so the error does not change from one build to the next.
    private static void ThrowIfAmbiguous(RouteDefinition[] definitions)
    {
        var latest = new Dictionary<RouteTemplate, int>(definitions.Length, SameTemplateComparer.SameShape);
        var previous = new int[definitions.Length];
        for (var i = 0; i < definitions.Length; i++)
        {
            RouteTemplate template = definitions[i].Template;
            int earliest = -1;
            string? commonPath = null;
            previous[i] = latest.TryGetValue(template, out int last) ? last : -1;
            // The built-in constraints overlap in classes, so at most one earlier member of a group can conflict with a
            // new one, and which is reported cannot be observed today. The loop still reports the earliest, so that
            // the documented behaviour holds for constraints that overlap without forming such classes.
            for (int j = previous[i]; j >= 0; j = previous[j])
            {
                string? path = definitions[j].Template.FindCommonPath(template);
                if (path is not null)
                    (earliest, commonPath) = (j, path);
            }

            if (commonPath is not null)
                throw Ambiguous(definitions[earliest], definitions[i], commonPath);

            latest[template] = i;
        }
    }

    private static InvalidOperationException Ambiguous(RouteDefinition earlier, RouteDefinition later, string path)
    {
        return new InvalidOperationException(
            $"The templates '{earlier.Template.Value}', mapped to route type '{earlier.RouteType}', and " +
            $"'{later.Template.Value}', mapped to route type '{later.RouteType}', rank equally and both match the " +
            $"path '{path}', so which route wins would depend on the order they were mapped in. " +
            (earlier.Template.HasSameRequiredSegments(later.Template)
                ? "Optional parameters never keep templates apart, because both templates match the path without " +
                  "them. Map the shorter path and each alternative as its own template with a required parameter, " +
                  "giving the alternatives constraints that no text satisfies together, or remove one."
                : "Keep them apart at a required position, with a different literal or with constraints there that " +
                  "no text satisfies together, or make them rank differently, or remove one."));
    }

    // Where the path ends and where the query starts are decided here together, so that the two always agree. The
    // query itself is not read: queryStart is -1 for an absolute URI, whose query is Uri.Query, and otherwise the index
    // in OriginalString of the '?' that starts it, or the length of the text when there is none, so that a URI that
    // matches nothing, and a route that reads no query values, pay nothing for it.
    // Tells an attached debugger that an exception the create function threw is not handled here, which the attribute
    // on Match would otherwise keep it from stopping on. The debugger takes that only from a method that has the
    // attribute, so this one has it too. It always declines the exception, and without a debugger it does nothing.
    [DebuggerDisableUserUnhandledExceptions]
    private static bool BreakInDebugger(Exception exception)
    {
        Debugger.BreakForUserUnhandledException(exception);
        return false;
    }

    private static string ReadPath(Uri uri, out int queryStart)
    {
        // A relative URI has no parsed parts: AbsolutePath and Query throw for one, so both are read from the text. On
        // Unix-like platforms, new Uri("/settings?tab=1") is an implicit file URI whose AbsolutePath escapes the '?'
        // into the path, so a file URI whose text starts with '/', where the caller wrote no file: scheme, is read
        // from its text too, as the app-relative link it was meant to be.
        if (uri.IsAbsoluteUri && !(uri.IsFile && uri.OriginalString.StartsWith('/')))
        {
            queryStart = -1;
            return uri.AbsolutePath;
        }

        // A fragment starts at the first '#' and a query at the first '?' before it, so the path ends at whichever
        // comes first.
        string text = uri.OriginalString;
        int end = text.AsSpan().IndexOfAny('?', '#');
        queryStart = end >= 0 && text[end] == '?' ? end : text.Length;
        return end < 0 ? text : text[..end];
    }
}
