namespace AdamE.AppNav.Routing;

// Turns template text into segments and enforces every rule of the template syntax, apart from the checks of the
// whole value that RouteTemplate.Parse makes first, so that the segment model can be read without the parsing rules.
internal static class RouteTemplateParser
{
    private const string InvalidParameterName =
        "has an invalid parameter name; a parameter name starts with an ASCII letter or '_', " +
        "followed by ASCII letters, digits or '_'";

    // The caller names the public argument the template came from, so that every exception thrown here reports it as
    // its ParamName: value for RouteTemplate.Parse, template for RouteTableBuilder.Map. resolveAdded is null for
    // RouteTemplate.Parse, which knows only the built-in constraints. A builder passes one that returns its constraint
    // for a name that is not built in but has the form of an added constraint's, bound or not, so that whether it is
    // ever added is left to Build and the order of the calls does not matter. It is called only for a segment that has
    // passed every check, though a later segment can still fail; a name it has seen then stays with the builder,
    // unbound, which changes nothing, since Build looks only at the constraints of the templates it was given.
    public static RouteSegment[] Parse(string value, string paramName, Func<string, RouteConstraint>? resolveAdded)
    {
        string[] segments = RouteText.SplitSegments(value);
        var parsed = new RouteSegment[segments.Length];
        // Each parameter name, ignoring case, with the index of the segment that used it, so that a repeated name can
        // quote the earlier segment as written.
        var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int firstOptional = -1;
        int catchAll = -1;

        for (var i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            ParameterText? parameter = FindParameter(segment);

            // A literal is decoded once, for the dot segment check and for the segment itself.
            string? literal = parameter is null ? Uri.UnescapeDataString(segment) : null;
            if (FindSegmentProblem(segment, parameter, literal, resolveAdded is not null) is { } problem)
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' {problem}.",
                    paramName);
            }

            // These two are checked before the repeated name because they depend only on the segment's position, and
            // apply to literals as well as parameters. At most one applies: a catch-all after an optional parameter is
            // rejected here, and nothing after a catch-all is accepted.
            if (catchAll >= 0)
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' follows the catch-all parameter " +
                    $"'{segments[catchAll]}'; a catch-all parameter must be the last segment.",
                    paramName);
            }

            if (firstOptional >= 0 && parameter is not { IsOptional: true })
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' follows the optional parameter " +
                    $"'{segments[firstOptional]}'; " +
                    (parameter is { IsCatchAll: true }
                        ? "a catch-all parameter cannot follow an optional parameter."
                        : "only optional parameters can follow an optional parameter."),
                    paramName);
            }

            if (parameter is not { } found)
            {
                parsed[i] = RouteSegment.Literal(literal!);
                continue;
            }

            string name = found.Name;
            if (!seenNames.TryAdd(name, i))
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' repeats the name of an earlier " +
                    $"parameter, '{segments[seenNames[name]]}'; parameter names are compared without regard to case.",
                    paramName);
            }

            if (found.IsOptional && firstOptional < 0)
                firstOptional = i;

            if (found.IsCatchAll)
                catchAll = i;

            // FindParameterProblem has rejected constraint text that is neither built in nor, with a resolver, of the
            // form of an added constraint's name, so any other text is resolved here.
            RouteConstraint? constraint = found.Constraint ??
                (found.ConstraintText is { } constraintText ? resolveAdded!(constraintText) : null);

            // A catch-all parameter is never optional and has no constraint, because FindParameterProblem rejects both.
            parsed[i] = found.IsCatchAll
                ? RouteSegment.CatchAllParameter(name)
                : found.IsOptional
                    ? RouteSegment.OptionalParameter(name, constraint)
                    : RouteSegment.Parameter(name, constraint);
        }

        return parsed;
    }

    // Returns the parameter's name and constraint text, split at the first ':' after a first '*' and a final '?' are
    // removed, the built-in constraint that text names, if any, which is null for the name of an added constraint,
    // and whether there was a first '*' and a final '?', when the segment is a whole-segment parameter, valid or not;
    // null when the segment is a literal.
    private static ParameterText? FindParameter(string segment)
    {
        if (segment.Length < 2 || segment[0] != '{' || segment[^1] != '}')
            return null;

        ReadOnlySpan<char> text = segment.AsSpan(1, segment.Length - 2);
        if (text.ContainsAny('{', '}'))
            return null;

        // Only the last '?' is removed, so any other '?' stays in the name or the constraint text to be reported.
        bool isOptional = text.Length > 0 && text[^1] == '?';
        if (isOptional)
            text = text[..^1];

        // Likewise only the first '*' is removed.
        bool isCatchAll = text.Length > 0 && text[0] == '*';
        if (isCatchAll)
            text = text[1..];

        int separator = text.IndexOf(':');
        if (separator < 0)
            return new ParameterText(text.ToString(), null, null, isOptional, isCatchAll);

        string constraintText = text[(separator + 1)..].ToString();
        return new ParameterText(
            text[..separator].ToString(),
            constraintText,
            RouteConstraint.Find(constraintText),
            isOptional,
            isCatchAll);
    }

    // The decoded literal is null for a parameter. canAddConstraints says whether the template is mapped on a builder,
    // which can add constraints of its own.
    private static string? FindSegmentProblem(
        string segment,
        ParameterText? parameter,
        string? literal,
        bool canAddConstraints)
    {
        // Checked first: a control character is invisible where the template is written, and its rule covers the
        // whole segment, so a tab or line feed at an edge is reported here rather than as white space.
        if (RouteText.ContainsControlCharacter(segment))
            return "contains a control character";

        // Checked next, for the same reason: a lone surrogate cannot be seen or percent-encoded, and its rule covers
        // the whole segment.
        if (RouteText.ContainsLoneSurrogate(segment))
            return "contains a lone surrogate, which is not valid UTF-16";

        if (parameter is { } found)
            return FindParameterProblem(found, canAddConstraints);

        // Checked before braces, so that a space next to an otherwise valid parameter, as in ' {id}', is reported as
        // the white space it is.
        if (char.IsWhiteSpace(segment[0]) || char.IsWhiteSpace(segment[^1]))
            return "starts or ends with white space";

        if (segment.Contains('{', StringComparison.Ordinal) || segment.Contains('}', StringComparison.Ordinal))
            return "contains '{' or '}' outside a parameter; a parameter is a whole segment, written '{name}'";

        if (segment.Contains('?', StringComparison.Ordinal) || segment.Contains('#', StringComparison.Ordinal))
            return "contains '?' or '#'";

        // Checked on the decoded text, unlike the rules above, because a path with '%2E%2E' is no more routable than
        // one with '..': Match refuses both.
        if (RouteText.IsDotSegment(literal))
            return "is a dot segment ('.' or '..' once decoded), " + RouteText.DotSegmentReason;

        return null;
    }

    private static string? FindParameterProblem(ParameterText parameter, bool canAddConstraints)
    {
        // Both checked across the name and the constraint, so that '{id?:int}' and '{id:in?t}' are reported alike.
        if (parameter.Name.Contains('*', StringComparison.Ordinal) || parameter.ConstraintText.AsSpan().Contains('*'))
        {
            return "contains '*' other than at the start; '*' is only allowed at the start of a parameter, written " +
                "'{*name}'";
        }

        if (parameter.Name.Contains('?', StringComparison.Ordinal) || parameter.ConstraintText.AsSpan().Contains('?'))
        {
            return "contains '?' other than at the end; '?' is only allowed at the end of a parameter, written " +
                "'{name?}' or '{name:constraint?}'";
        }

        if (!IsParameterName(parameter.Name))
            return InvalidParameterName;

        // Both checked before the constraint text, which a catch-all cannot have at all, so that '{*rest:slug}' is
        // reported as having a constraint rather than an unknown one, which would suggest the wrong fix.
        if (parameter is { IsCatchAll: true, IsOptional: true })
        {
            return "is an optional catch-all parameter; a catch-all parameter cannot be optional, because it " +
                "already matches when nothing is left of the path";
        }

        if (parameter is { IsCatchAll: true, ConstraintText: not null })
            return "is a catch-all parameter with a constraint; a catch-all parameter cannot have a constraint";

        if (parameter.ConstraintText is not { } constraintText)
            return null;

        // The constraint is the text after the first ':', so '{id::int}' and '{id:int:}' have a constraint that is not
        // empty but contains ':', and are reported as the extra ':' rather than as an empty or unknown constraint.
        if (constraintText.Contains(':', StringComparison.Ordinal))
            return "contains more than one ':'; a parameter is written '{name}' or '{name:constraint}'";

        if (constraintText.Length == 0)
            return "has no constraint after ':'; a parameter is written '{name}' or '{name:constraint}'";

        if (parameter.Constraint is not null)
            return null;

        // A reserved name can never be added, so it is rejected here on a builder too, and the message says why rather
        // than pointing to AddConstraint.
        if (RouteConstraint.IsReserved(constraintText))
        {
            return $"has an unknown constraint '{constraintText}'; the supported constraints are " +
                $"{RouteConstraint.SupportedNames}, ignoring case. The name " +
                $"'{constraintText.ToLowerInvariant()}' is reserved: {RouteConstraint.ReservedReason}";
        }

        // On a builder, a name that a constraint could be added under is left for Build to find added or not, since
        // AddConstraint can be called after Map. Any other text can never name a constraint, so it is rejected here.
        if (!canAddConstraints)
        {
            return $"has an unknown constraint '{constraintText}'; the supported constraints are " +
                $"{RouteConstraint.SupportedNames}, ignoring case. A constraint added with " +
                "RouteTableBuilder.AddConstraint is known only to the templates mapped on the builder it was added to";
        }

        if (!RouteConstraint.IsAddableName(constraintText))
        {
            return $"has an unknown constraint '{constraintText}'; the supported constraints are " +
                $"{RouteConstraint.SupportedNames}, ignoring case, and a constraint added with AddConstraint has a " +
                "name of an ASCII letter followed by ASCII letters, digits, '-' or '_'";
        }

        return null;
    }

    private static bool IsParameterName(string name)
    {
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(name[i]) || name[i] == '_'))
                return false;
        }

        return true;
    }

    // A parameter segment's text, without a first '*' or a final '?', split at the first ':' into the name and, when
    // there is a ':', the constraint text, with the built-in constraint that the text names, or null when it names
    // none, as the name of an added constraint does.
    private readonly record struct ParameterText(
        string Name,
        string? ConstraintText,
        RouteConstraint? Constraint,
        bool IsOptional,
        bool IsCatchAll);
}
