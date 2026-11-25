using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace AdamE.AppNav.Routing;

/// <summary>
/// Matches URIs to typed routes, using the templates mapped by a <see cref="RouteTableBuilder"/>.
/// </summary>
/// <remarks>
/// A table is immutable once built, and safe to use from several threads at once: <see cref="Match(Uri)"/> and
/// <see cref="Format(AppRoute)"/> read nothing that can change. A create function, or a value function of a format
/// function, that is called from several threads must itself be safe to call that way.
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
                $"The create function for route type '{definition.RouteType}', mapped to the template " +
                $"'{definition.Template.Value}', returned null; it must return a route."),
            definition);
    }

    /// <summary>
    /// Writes a route as the path of the template it is mapped to, by the format function its mapping was given.
    /// </summary>
    /// <param name="route">The route to write.</param>
    /// <returns>
    /// The route as the text of a relative URI: its path, as
    /// <see cref="RouteTemplate.Format(IReadOnlyDictionary{string, string})"/> writes it for the values the format
    /// function reads from the route, each written as the canonical text of its type that
    /// <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/> describes, such as
    /// <c>/orders/42</c>. A later version will also write the route's query; this version writes the path only.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The mapping is found by the exact type of the route, as <see cref="object.GetType()"/> gives it: a mapping of a
    /// base type of the route does not write it. Each declared value function is called at most once, in template
    /// order; after a value that cannot be written as text, or an exception from a value function, the later
    /// functions are not called. A <see langword="null"/> value leaves out an optional or catch-all parameter.
    /// </para>
    /// <para>
    /// When this method returns a path, <see cref="Match(Uri)"/> of that path selects the mapping that wrote it, and
    /// each value reads back equal through the typed read of its type, so the route that matches is equal to the one
    /// written provided the create function and the format function agree, which the table cannot check. A path that
    /// a template ranking higher would match first is not returned: with <c>/products/new</c> and
    /// <c>/products/{name}</c> in one table, the name <c>new</c> cannot be written, because <c>/products/new</c> would
    /// open the other route. Which template a path selects is decided exactly as <see cref="Match(Uri)"/> decides it,
    /// by the templates alone, and no create function is called. That check comes after every other, and calls no
    /// value function. A mapping of the same route type that ranks higher also counts, because the table cannot know
    /// that its create function makes the same route.
    /// </para>
    /// <para>
    /// The promise is about <see cref="Match(Uri)"/> of the returned text as a relative URI, as in
    /// <c>new Uri(path, UriKind.Relative)</c>, and of an absolute URI with that path, except where <see cref="Uri"/>
    /// itself changes the path of an absolute URI. It does so in one case, described in the remarks on
    /// <see cref="Match(Uri)"/>: it drops <c>%20</c> sequences at the very end. So a value that ends with a space,
    /// written last in the path, may be read back without it from an absolute link, and may then match another
    /// template: with <c>/&#233;/new</c> and <c>/&#233;/{name}</c>, the name <c>new </c> is written as
    /// <c>/%C3%A9/new%20</c>, which matches as that name as a relative URI, while
    /// <c>https://app/%C3%A9/new%20</c> opens the route of <c>/&#233;/new</c>.
    /// </para>
    /// <para>
    /// The table is not changed, and several threads can call this method and <see cref="Match(Uri)"/> at once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No mapping of exactly the type of <paramref name="route"/> has a format function. The message names the type
    /// and says whether the type is not mapped at all, or is mapped but without a format function.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A value of the route cannot be written: a required parameter's value is <see langword="null"/>; a value is
    /// empty, contains a lone surrogate, does not satisfy the parameter's constraint or is a dot segment, <c>.</c> or
    /// <c>..</c>; a catch-all value starts or ends with <c>/</c>, contains <c>//</c> or has a dot segment as a part; an
    /// optional parameter has a value although an earlier one is left out; or a value has no text, such as a
    /// <see cref="double"/> that is NaN or an enum value that is not the value of a member. The exception's
    /// <see cref="ArgumentException.ParamName"/> is <c>route</c>, and its message names the route type, the template
    /// and the parameter and says what is wrong, but never quotes the value, which can be a secret, such as a sign-in
    /// token. It has no inner exception. When more than one value cannot be written, which one is reported is not
    /// specified.
    /// <para>-or-</para>
    /// The path written for the route is matched first by the template of another mapping, one that ranks higher, so
    /// <see cref="Match(Uri)"/> of it would select that mapping instead, whether or not that mapping has a format
    /// function. The exception's <see cref="ArgumentException.ParamName"/> is <c>route</c>, and its message names the
    /// route type and both templates, but neither the route's values nor the path. It has no inner exception. To fix
    /// the table, change the templates so that they cannot match the same path, for example by constraining a
    /// parameter or by giving one of them a literal of its own.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that a value function throws, unchanged.
    /// </exception>
    public string Format(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        Type type = route.GetType();
        if (!_formatters.TryGetValue(type, out (RouteFormatter Formatter, int Position) writer))
        {
            throw new InvalidOperationException(_routeTypes.Contains(type)
                ? $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written as a path, because it is " +
                  "mapped, but none of its mappings has a format function. Pass one to the RouteTableBuilder.Map " +
                  "overload that takes it; for a template with no parameters, a function that declares nothing, " +
                  "as in '_ => { }'."
                : $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written as a path, because no " +
                  "mapping of exactly that type exists. A mapping of a base type does not write a route of a type " +
                  "derived from it; map the type itself with a format function.");
        }

        string path = writer.Formatter.Format(route);

        // A definition ranked first has none above it that could match the path first, so it pays nothing more.
        RouteDefinition? first = writer.Position == 0 ? null : FindMatchedFirst(path, writer.Position);
        if (first is null)
            return path;

        // The message names both templates, but neither the route's values nor the path, which can carry a secret.
        // The capturing template can contain a literal equal to a value, as '/products/new' does for the name 'new',
        // but such a value is text the application wrote into its own template, not a secret. It says which template
        // matches, not which route opens, because that template's create function may still reject the value.
        RouteDefinition owner = _matchOrder[writer.Position];
        throw new ArgumentException(
            $"The route type '{RouteValueTypes.DisplayName(type)}' cannot be written as a path by the template " +
            $"'{owner.Template.Value}': the path it writes is matched first by the template " +
            $"'{first.Template.Value}', which ranks higher, so a link to it would be matched by that template " +
            "instead. Change the templates so that they cannot match the same path, for example by constraining a " +
            "parameter or by giving one of them a literal of its own.",
            nameof(route));
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
    // reports an enum that is not registered. Each check runs in the order the routes were mapped, so the error does
    // not change from one build to the next.
    private static FrozenDictionary<Type, (RouteFormatter Formatter, int Position)> BindFormatters(
        RouteDefinition[] definitions,
        int[] positions,
        FrozenDictionary<Type, object> codecs)
    {
        // Every second format function is reported before anything is bound, so that it is reported even when a
        // format function also writes an enum that is not registered: registering the enum would not make the table
        // valid, and fixing it would take two rounds.
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
