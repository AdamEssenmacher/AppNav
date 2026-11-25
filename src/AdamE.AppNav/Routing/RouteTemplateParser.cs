namespace AdamE.AppNav.Routing;

// Turns template text into segments and enforces every rule of the template syntax, apart from the checks of the
// whole value that RouteTemplate.Parse makes first, so that the segment model can be read without the parsing rules.
internal static class RouteTemplateParser
{
    private const string InvalidParameterName =
        "has an invalid parameter name; a parameter name starts with an ASCII letter or '_', " +
        "followed by ASCII letters, digits or '_'";

    // The parameter is named value on purpose: it matches the parameter of RouteTemplate.Parse, so that nameof(value)
    // gives every exception thrown here the ParamName of the public method's argument.
    public static RouteSegment[] Parse(string value)
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
            if (FindSegmentProblem(segment, parameter) is { } problem)
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' {problem}.",
                    nameof(value));
            }

            // These two are checked before the repeated name because they depend only on the segment's position, and
            // apply to literals as well as parameters. At most one applies: a catch-all after an optional parameter is
            // rejected here, and nothing after a catch-all is accepted.
            if (catchAll >= 0)
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' follows the catch-all parameter " +
                    $"'{segments[catchAll]}'; a catch-all parameter must be the last segment.",
                    nameof(value));
            }

            if (firstOptional >= 0 && parameter is not { IsOptional: true })
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' follows the optional parameter " +
                    $"'{segments[firstOptional]}'; " +
                    (parameter is { IsCatchAll: true }
                        ? "a catch-all parameter cannot follow an optional parameter."
                        : "only optional parameters can follow an optional parameter."),
                    nameof(value));
            }

            if (parameter is not { } found)
            {
                parsed[i] = RouteSegment.Literal(Uri.UnescapeDataString(segment));
                continue;
            }

            string name = found.Name;
            if (!seenNames.TryAdd(name, i))
            {
                throw new ArgumentException(
                    $"Route template segment '{RouteText.EscapeForMessage(segment)}' repeats the name of an earlier " +
                    $"parameter, '{segments[seenNames[name]]}'; parameter names are compared without regard to case.",
                    nameof(value));
            }

            if (found.IsOptional && firstOptional < 0)
                firstOptional = i;

            if (found.IsCatchAll)
                catchAll = i;

            // A catch-all parameter is never optional and has no constraint, because FindParameterProblem rejects both.
            parsed[i] = found.IsCatchAll
                ? RouteSegment.CatchAllParameter(name)
                : found.IsOptional
                    ? RouteSegment.OptionalParameter(name, found.Constraint)
                    : RouteSegment.Parameter(name, found.Constraint);
        }

        return parsed;
    }

    // Returns the parameter's name and constraint text, split at the first ':' after a first '*' and a final '?' are
    // removed, the built-in constraint that text names, if any, and whether there was a first '*' and a final '?',
    // when the segment is a whole-segment parameter, valid or not; null when the segment is a literal.
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

    private static string? FindSegmentProblem(string segment, ParameterText? parameter)
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
            return FindParameterProblem(found);

        // Checked before braces, so that a space next to an otherwise valid parameter, as in ' {id}', is reported as
        // the white space it is.
        if (char.IsWhiteSpace(segment[0]) || char.IsWhiteSpace(segment[^1]))
            return "starts or ends with white space";

        if (segment.Contains('{', StringComparison.Ordinal) || segment.Contains('}', StringComparison.Ordinal))
            return "contains '{' or '}' outside a parameter; a parameter is a whole segment, written '{name}'";

        if (segment.Contains('?', StringComparison.Ordinal) || segment.Contains('#', StringComparison.Ordinal))
            return "contains '?' or '#'";

        return null;
    }

    private static string? FindParameterProblem(ParameterText parameter)
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

        if (parameter.Constraint is null)
        {
            return $"has an unknown constraint '{constraintText}'; the supported constraints are " +
                $"{RouteConstraint.SupportedNames}, ignoring case";
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
    // none.
    private readonly record struct ParameterText(
        string Name,
        string? ConstraintText,
        RouteConstraint? Constraint,
        bool IsOptional,
        bool IsCatchAll);
}
