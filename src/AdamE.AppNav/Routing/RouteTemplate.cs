using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace AdamE.AppNav.Routing;

/// <summary>
/// A parsed route template: the path pattern that a route answers to, such as <c>/products/{id}</c>.
/// </summary>
/// <remarks>
/// <para>
/// A template is a path that starts with <c>/</c> and is divided into segments by <c>/</c>. A segment written
/// <c>{name}</c> is a parameter: it matches any one path segment and captures that segment's decoded text under its
/// name. Every other segment is a literal that a path must contain exactly, in the same position.
/// </para>
/// <para>
/// A parameter name starts with an ASCII letter or an underscore, followed by ASCII letters, digits or underscores.
/// Names are compared without regard to case, so a template cannot use two names that differ only by case, and
/// captured values are looked up the same way.
/// </para>
/// <para>
/// A parameter written <c>{name:constraint}</c> matches a segment only when the segment's decoded text satisfies the
/// constraint, one of the built-in constraints <c>int</c>, <c>long</c>, <c>decimal</c>, <c>bool</c>, <c>guid</c> and
/// <c>alpha</c>, whose names are compared without regard to case. Each checks the text exactly as decoded, without
/// trimming, and independently of culture: <c>int</c> and <c>long</c> accept an optional leading <c>+</c> or
/// <c>-</c> and ASCII digits within the type's range; <c>decimal</c> also accepts one <c>.</c>; <c>bool</c> accepts
/// <c>true</c> or <c>false</c> in any ASCII case; <c>guid</c> accepts 32 hex digits, with or without hyphens in the
/// 8-4-4-4-12 form; and <c>alpha</c> accepts one or more Unicode letters. The captured value is the decoded text,
/// unchanged.
/// </para>
/// <para>
/// A parameter written <c>{name?}</c> or <c>{name:constraint?}</c> is optional: it matches one segment or none. The
/// <c>?</c> must be the last character inside the braces. Optional parameters must be trailing: every segment after
/// an optional parameter must also be an optional parameter, so <c>/products/{id?}</c> and <c>/a/{b?}/{c?}</c> are
/// valid and <c>/{id?}/details</c> is not. A path may leave out optional parameters from the end only, so the
/// present segments fill the template's positions from left to right.
/// </para>
/// <para>
/// Templates and paths are divided the same way. Leading, trailing and repeated slashes do not create segments, so
/// <c>/a//b/</c> has the same segments as <c>/a/b</c>, and <c>/</c> is the root template, which has no segments.
/// Each segment is then percent-decoded once, so <c>%2F</c> is part of its segment and does not divide it. A percent
/// sequence that is not valid, or does not decode to valid UTF-8, is kept as written.
/// </para>
/// <para>
/// A segment cannot contain a control character (one for which <see cref="char.IsControl(char)"/> is
/// <see langword="true"/>, such as a tab or line feed). A literal segment also cannot start or end with white space,
/// or contain <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>. To match those characters, percent-encode them in the
/// template: for example <c>%09</c> for a tab, <c>%0A</c> for a line feed, <c>%00</c> for a null character,
/// <c>%20</c>, <c>%7B</c>, <c>%7D</c>, <c>%3F</c> and <c>%23</c>.
/// </para>
/// <para>
/// A segment also cannot contain a lone surrogate, a <see langword="char"/> that is half of a surrogate pair without
/// its other half, because such text is not valid UTF-16 and cannot be percent-encoded. A surrogate has no UTF-8
/// bytes of its own, and the three bytes it would encode to, such as <c>%ED%A0%80</c>, are not valid UTF-8, so that
/// text decodes to itself and the literal is that percent text.
/// </para>
/// </remarks>
public sealed class RouteTemplate
{
    private const string InvalidParameterName =
        "has an invalid parameter name; a parameter name starts with an ASCII letter or '_', " +
        "followed by ASCII letters, digits or '_'";

    private readonly Segment[] _segments;
    private readonly int _parameterCount;

    // The number of segments before the first optional parameter, which is the fewest segments a matching path has.
    private readonly int _requiredCount;

    private RouteTemplate(string value, Segment[] segments, int parameterCount, int requiredCount)
    {
        Value = value;
        _segments = segments;
        _parameterCount = parameterCount;
        _requiredCount = requiredCount;
    }

    /// <summary>
    /// Gets the template text exactly as it was passed to <see cref="Parse(string)"/>.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Parses a route template.
    /// </summary>
    /// <param name="value">The template text. It must start with <c>/</c>.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <para>
    /// <paramref name="value"/> is empty or consists only of white space or does not start with <c>/</c>; a segment
    /// contains a control character or a lone surrogate; a parameter contains <c>*</c>, contains <c>?</c> other than
    /// as its last character, has an invalid name, repeats the name of an earlier parameter, ignoring case, contains
    /// more than one <c>:</c>, or has an empty or unknown constraint; a literal segment starts or ends with white
    /// space, or contains <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>; or a literal or a parameter that is not optional
    /// follows an optional parameter. These checks apply to the text as written, before percent-decoding.
    /// </para>
    /// <para>
    /// The first invalid segment, reading left to right, is reported. Within a segment, a control character is
    /// reported first, then a lone surrogate, then any other problem of the segment's own text, then a segment that
    /// follows an optional parameter without being one, then a repeated name. Within a parameter, <c>*</c> is
    /// reported first, then a <c>?</c> that is not last, then, with a final <c>?</c> removed, an invalid name, then
    /// more than one <c>:</c>, then an empty constraint, then an unknown one. A control character is reported even
    /// when it is also white space, such as a tab at the start of a segment. The message shows each control character
    /// and lone surrogate as a <c>\uXXXX</c> escape.
    /// </para>
    /// </exception>
    public static RouteTemplate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!value.StartsWith('/'))
            throw new ArgumentException("Route templates must start with '/'.", nameof(value));

        string[] segments = SplitSegments(value);
        var parsed = new Segment[segments.Length];
        // Each parameter name, ignoring case, with the index of the segment that used it, so that a repeated name can
        // quote the earlier segment as written.
        var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int firstOptional = -1;

        for (var i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            ParameterText? parameter = FindParameter(segment);
            if (FindSegmentProblem(segment, parameter) is { } problem)
            {
                throw new ArgumentException(
                    $"Route template segment '{EscapeForMessage(segment)}' {problem}.",
                    nameof(value));
            }

            // Checked before the repeated name because it depends only on the segment's position, and applies to
            // literals as well as parameters.
            if (firstOptional >= 0 && parameter is not { IsOptional: true })
            {
                throw new ArgumentException(
                    $"Route template segment '{EscapeForMessage(segment)}' follows the optional parameter " +
                    $"'{segments[firstOptional]}'; only optional parameters can follow an optional parameter.",
                    nameof(value));
            }

            if (parameter is not { Name: var name, Constraint: var constraint, IsOptional: var isOptional })
            {
                parsed[i] = Segment.Literal(Uri.UnescapeDataString(segment));
                continue;
            }

            if (!seenNames.TryAdd(name, i))
            {
                throw new ArgumentException(
                    $"Route template segment '{EscapeForMessage(segment)}' repeats the name of an earlier " +
                    $"parameter, '{segments[seenNames[name]]}'; parameter names are compared without regard to case.",
                    nameof(value));
            }

            if (isOptional && firstOptional < 0)
                firstOptional = i;

            parsed[i] = Segment.Parameter(name, constraint, isOptional);
        }

        return new RouteTemplate(value, parsed, seenNames.Count, firstOptional < 0 ? parsed.Length : firstOptional);
    }

    /// <summary>
    /// Matches a path against this template.
    /// </summary>
    /// <param name="path">
    /// The path to match, such as <c>/products/42</c>. A leading <c>/</c> is optional. The whole value is the path: it
    /// is not trimmed, and a query string or fragment is not removed.
    /// </param>
    /// <returns>
    /// The values captured by the template's parameters, keyed by parameter name as written in the template and
    /// looked up without regard to case, or <see langword="null"/> when the path does not match. An optional
    /// parameter that the path leaves out has no entry. Each match that captures a value returns its own dictionary;
    /// treat it as read-only. When nothing is captured, the dictionary is empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A path matches when it has at least as many segments as the template has before its first optional parameter
    /// and no more than the template has in all, and each segment the path has is accepted by the template segment in
    /// the same position: a decoded segment in a literal's position equals that literal, compared ordinally and
    /// case-sensitively, and a decoded segment in a parameter's position satisfies the parameter's constraint, if it
    /// has one. The optional parameters after the path's last segment are left out.
    /// </para>
    /// <para>
    /// A segment that does not satisfy a constraint is not an error: the path simply does not match, even when the
    /// parameter is optional, because a segment that is present is never treated as left out. A captured value is the
    /// decoded path segment, unchanged even when a constraint checked it, so it contains <c>/</c> where the path has
    /// <c>%2F</c>. White space is significant, <c>+</c> is not a space, and <c>.</c> and <c>..</c> are ordinary
    /// segments.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, string>? Match(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] pathSegments = SplitSegments(path);
        if (pathSegments.Length < _requiredCount || pathSegments.Length > _segments.Length)
            return null;

        // Every segment is decoded in place and checked, against its literal or its parameter's constraint, before the
        // dictionary is allocated, so a path that does not match allocates no dictionary.
        for (var i = 0; i < pathSegments.Length; i++)
        {
            string decoded = Uri.UnescapeDataString(pathSegments[i]);
            if (!_segments[i].Accepts(decoded))
                return null;

            pathSegments[i] = decoded;
        }

        // The segments the path leaves out are all optional parameters, because only optional parameters are trailing.
        int capturedCount = _parameterCount - (_segments.Length - pathSegments.Length);
        if (capturedCount == 0)
            return ReadOnlyDictionary<string, string>.Empty;

        var values = new Dictionary<string, string>(capturedCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pathSegments.Length; i++)
        {
            Segment segment = _segments[i];
            if (segment.IsParameter)
                values.Add(segment.Text, pathSegments[i]);
        }

        return values;
    }

    /// <summary>
    /// Builds the path that this template answers to, using the given parameter values.
    /// </summary>
    /// <param name="pathValues">
    /// The value of each of the template's parameters, keyed by parameter name without regard to case, as everywhere
    /// else in a template. Each name is looked up as written in the template first; only if that finds nothing is the
    /// dictionary searched for a key that equals the name ignoring case. Entries for names the template does not use
    /// are ignored. An optional parameter is left out when it has no entry, neither under its name as written nor
    /// under a key that equals it ignoring case.
    /// </param>
    /// <returns>
    /// The path: <c>/</c> followed by the segments separated by <c>/</c>, with no trailing slash; <c>/</c> for the
    /// root template, and when the template has only optional parameters and none is given. Each literal is written as
    /// its decoded text and each parameter as its value, both percent-encoded with
    /// <see cref="Uri.EscapeDataString(string)"/>, so a <c>/</c> in a value is written as <c>%2F</c>. An optional
    /// parameter that is left out is not written, and neither is anything after it, so <c>/products/{id?}</c> formats
    /// as <c>/products</c> without an <c>id</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="Match(string)"/> reads back what this method writes: for every template and every dictionary this
    /// method accepts, matching the formatted path returns exactly one entry for each required parameter and each
    /// optional parameter that was given, with the values that were formatted. In the other direction, formatting the
    /// values that a match returned produces a path that matches again with the same values, unless the matched path
    /// itself contained a lone surrogate in a parameter's position: <see cref="Match(string)"/> does not check paths,
    /// and this method rejects such a value.
    /// </para>
    /// <para>
    /// That path need not have the same text as the path that was matched. The output is canonical: the same values
    /// always format to the same path, whatever path they were matched from. Every segment is encoded the same way,
    /// whether or not the original text was encoded, so <c>/my files</c> and <c>/my%20files</c> both format as
    /// <c>/my%20files</c>, and repeated slashes are not reproduced.
    /// </para>
    /// <para>
    /// A value for a parameter with a constraint must satisfy it, so that the path matches. Values are not otherwise
    /// checked: white space, control characters, <c>%</c>, <c>/</c> and any other text are encoded and read back
    /// unchanged. <c>.</c> and <c>..</c> are written as they are, as values or literals, and a URI parser or browser
    /// may remove them and the segment before them when the path is resolved.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pathValues"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A parameter that is not optional has no entry in <paramref name="pathValues"/>; a parameter has no entry under
    /// its name as written and more than one whose key equals it ignoring case; a parameter's value is
    /// <see langword="null"/>, empty, not valid UTF-16 (it contains a lone surrogate, which cannot be percent-encoded
    /// and read back unchanged), or does not satisfy the parameter's constraint; or an optional parameter has a value
    /// although an earlier optional parameter was left out, so its segment cannot be written. These rules apply to
    /// optional parameters that have an entry exactly as to other parameters: a <see langword="null"/> or empty value
    /// does not leave a parameter out. The first parameter in template order that cannot be formatted is the one
    /// reported, and a value that is not valid is reported before it is found to follow a parameter that was left out.
    /// </exception>
    public string Format(IReadOnlyDictionary<string, string> pathValues)
    {
        ArgumentNullException.ThrowIfNull(pathValues);

        if (_segments.Length == 0)
            return "/";

        var builder = new StringBuilder(Value.Length + (16 * _parameterCount));
        string? leftOut = null;
        foreach (Segment segment in _segments)
        {
            if (!segment.IsParameter)
            {
                builder.Append('/').Append(segment.EncodedText);
                continue;
            }

            // Every later parameter is still looked up and checked after one is left out, so that a value given for
            // one of them is reported rather than silently dropped.
            string? value = GetPathValue(pathValues, segment);
            if (value is null)
            {
                leftOut ??= segment.Text;
                continue;
            }

            if (leftOut is not null)
            {
                throw new ArgumentException(
                    $"Path parameter '{segment.Text}' has a value, but it cannot be written because the earlier " +
                    $"optional parameter '{leftOut}' is left out.",
                    nameof(pathValues));
            }

            builder.Append('/').Append(Uri.EscapeDataString(value));
        }

        return builder.Length == 0 ? "/" : builder.ToString();
    }

    // Returns null only for an optional parameter that has no entry.
    private static string? GetPathValue(IReadOnlyDictionary<string, string> pathValues, Segment parameter)
    {
        string name = parameter.Text;
        if (!pathValues.TryGetValue(name, out string? value) && !TryGetValueIgnoringCase(pathValues, name, out value))
        {
            return parameter.IsOptional
                ? null
                : throw new ArgumentException($"Path parameter '{name}' has no value.", nameof(pathValues));
        }

        // A null or empty value does not leave an optional parameter out, because Match never returns one; saying how
        // to leave it out keeps that from looking like a bug.
        const string HowToLeaveOut = "; to leave out an optional parameter, leave it out of the dictionary";
        if (value is null)
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has a null value{(parameter.IsOptional ? HowToLeaveOut : "")}.",
                nameof(pathValues));
        }

        if (value.Length == 0)
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has an empty value" +
                (parameter.IsOptional ? HowToLeaveOut : "; a parameter always matches exactly one path segment") + ".",
                nameof(pathValues));
        }

        if (ContainsLoneSurrogate(value))
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has a value that is not valid UTF-16: it contains a lone surrogate, " +
                "which cannot be percent-encoded and read back unchanged.",
                nameof(pathValues));
        }

        if (parameter.Constraint is { } constraint && !constraint.IsSatisfiedBy(value))
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has a value that does not satisfy its constraint '{constraint.Name}'.",
                nameof(pathValues));
        }

        return value;
    }

    // Only reached when the name as written is not a key, which is also how an optional parameter is left out. Reads
    // every entry, except that it reads none when nothing can be found: in an empty dictionary, or in a Dictionary
    // that compares keys without regard to case, such as the one Match returns, whose own lookup has already tried
    // every spelling of the name.
    private static bool TryGetValueIgnoringCase(
        IReadOnlyDictionary<string, string> pathValues,
        string name,
        out string? value)
    {
        value = null;
        if (pathValues.Count == 0 ||
            (pathValues is Dictionary<string, string> dictionary &&
             dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        string? foundKey = null;
        foreach (KeyValuePair<string, string> entry in pathValues)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(entry.Key, name))
                continue;

            if (foundKey is not null)
            {
                throw new ArgumentException(
                    $"Path parameter '{name}' is ambiguous: keys '{EscapeForMessage(foundKey)}' and " +
                    $"'{EscapeForMessage(entry.Key)}' both equal it ignoring case.",
                    nameof(pathValues));
            }

            foundKey = entry.Key;
            value = entry.Value;
        }

        return foundKey is not null;
    }

    private static bool ContainsLoneSurrogate(string value)
    {
        int start = value.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF');
        if (start < 0)
            return false;

        for (int i = start; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                i++;
            else if (char.IsSurrogate(value[i]))
                return true;
        }

        return false;
    }

    // Returns the parameter's name and constraint text, split at the first ':' after a final '?' is removed, the
    // built-in constraint that text names, if any, and whether there was a final '?', when the segment is a
    // whole-segment parameter, valid or not; null when the segment is a literal.
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

        int separator = text.IndexOf(':');
        if (separator < 0)
            return new ParameterText(text.ToString(), null, null, isOptional);

        string constraintText = text[(separator + 1)..].ToString();
        return new ParameterText(
            text[..separator].ToString(),
            constraintText,
            RouteConstraint.Find(constraintText),
            isOptional);
    }

    private static string? FindSegmentProblem(string segment, ParameterText? parameter)
    {
        // Checked first: a control character is invisible where the template is written, and its rule covers the
        // whole segment, so a tab or line feed at an edge is reported here rather than as white space.
        if (ContainsControlCharacter(segment))
            return "contains a control character";

        // Checked next, for the same reason: a lone surrogate cannot be seen or percent-encoded, and its rule covers
        // the whole segment.
        if (ContainsLoneSurrogate(segment))
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
            return "contains '*', which is not allowed in a parameter";

        if (parameter.Name.Contains('?', StringComparison.Ordinal) || parameter.ConstraintText.AsSpan().Contains('?'))
        {
            return "contains '?' other than at the end; '?' is only allowed at the end of a parameter, written " +
                "'{name?}' or '{name:constraint?}'";
        }

        if (!IsParameterName(parameter.Name))
            return InvalidParameterName;

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

    private static bool ContainsControlCharacter(string segment)
    {
        foreach (char c in segment)
        {
            if (char.IsControl(c))
                return true;
        }

        return false;
    }

    // Writes each control character and each lone surrogate as a \uXXXX escape, so that quoting a segment in a
    // message cannot break the line or the terminal it is printed to, or produce text that is not valid UTF-16. Other
    // characters, including valid surrogate pairs, are quoted as written.
    private static string EscapeForMessage(string segment)
    {
        if (!ContainsControlCharacter(segment) && !ContainsLoneSurrogate(segment))
            return segment;

        var builder = new StringBuilder(segment.Length + 8);
        for (var i = 0; i < segment.Length; i++)
        {
            char c = segment[i];
            if (char.IsHighSurrogate(c) && i + 1 < segment.Length && char.IsLowSurrogate(segment[i + 1]))
                builder.Append(c).Append(segment[++i]);
            else if (char.IsControl(c) || char.IsSurrogate(c))
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            else
                builder.Append(c);
        }

        return builder.ToString();
    }

    private static string[] SplitSegments(string path)
    {
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    // A parameter segment's text, without a final '?', split at the first ':' into the name and, when there is a ':',
    // the constraint text, with the built-in constraint that the text names, or null when it names none.
    private readonly record struct ParameterText(
        string Name,
        string? ConstraintText,
        RouteConstraint? Constraint,
        bool IsOptional);

    private readonly struct Segment
    {
        private Segment(string text, string encodedText, bool isParameter, RouteConstraint? constraint, bool isOptional)
        {
            Text = text;
            EncodedText = encodedText;
            IsParameter = isParameter;
            Constraint = constraint;
            IsOptional = isOptional;
        }

        // A literal's decoded text, which path segments are compared with, or a parameter's name.
        public string Text { get; }

        // A literal's text percent-encoded as Format writes it; empty for a parameter.
        public string EncodedText { get; }

        public bool IsParameter { get; }

        // A parameter's constraint; null for a literal or a parameter without one.
        public RouteConstraint? Constraint { get; }

        // Whether this is a parameter written with a final '?', which a path may leave out.
        public bool IsOptional { get; }

        public static Segment Literal(string text)
        {
            return new Segment(text, Uri.EscapeDataString(text), false, null, false);
        }

        public static Segment Parameter(string name, RouteConstraint? constraint, bool isOptional)
        {
            return new Segment(name, string.Empty, true, constraint, isOptional);
        }

        // Whether a decoded path segment can stand in this segment's position.
        public bool Accepts(string decoded)
        {
            return IsParameter
                ? Constraint is null || Constraint.IsSatisfiedBy(decoded)
                : StringComparer.Ordinal.Equals(Text, decoded);
        }
    }
}
