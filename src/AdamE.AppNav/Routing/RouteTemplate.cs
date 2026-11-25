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

    private RouteTemplate(string value, Segment[] segments, int parameterCount)
    {
        Value = value;
        _segments = segments;
        _parameterCount = parameterCount;
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
    /// contains a control character or a lone surrogate; a parameter has an invalid name or repeats the name of an
    /// earlier parameter, ignoring case; or a literal segment starts or ends with white space, or contains <c>{</c>,
    /// <c>}</c>, <c>?</c> or <c>#</c>. These checks apply to the text as written, before percent-decoding.
    /// </para>
    /// <para>
    /// The first invalid segment, reading left to right, is reported. Within a segment, a control character is
    /// reported first, then a lone surrogate, then any other problem. A control character is reported even when it is
    /// also white space, such as a tab at the start of a segment. The message shows each control character and lone
    /// surrogate as a <c>\uXXXX</c> escape.
    /// </para>
    /// </exception>
    public static RouteTemplate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!value.StartsWith('/'))
            throw new ArgumentException("Route templates must start with '/'.", nameof(value));

        string[] segments = SplitSegments(value);
        var parsed = new Segment[segments.Length];
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            string? name = FindParameterName(segment);
            if (FindSegmentProblem(segment, name) is { } problem)
            {
                throw new ArgumentException(
                    $"Route template segment '{EscapeForMessage(segment)}' {problem}.",
                    nameof(value));
            }

            if (name is null)
            {
                parsed[i] = Segment.Literal(Uri.UnescapeDataString(segment));
                continue;
            }

            if (!seenNames.Add(name))
            {
                seenNames.TryGetValue(name, out string? earlier);
                throw new ArgumentException(
                    $"Route template segment '{EscapeForMessage(segment)}' repeats the name of an earlier " +
                    $"parameter, '{{{earlier}}}'; parameter names are compared without regard to case.",
                    nameof(value));
            }

            parsed[i] = Segment.Parameter(name);
        }

        return new RouteTemplate(value, parsed, seenNames.Count);
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
    /// looked up without regard to case, or <see langword="null"/> when the path does not match. Each match returns
    /// its own dictionary; treat it as read-only. For a template without parameters, the dictionary is empty.
    /// </returns>
    /// <remarks>
    /// A path matches when it has the same number of segments as the template, each decoded segment in a literal's
    /// position equals that literal, compared ordinally and case-sensitively, and each parameter's position holds one
    /// segment. A captured value is the decoded path segment, so it contains <c>/</c> where the path has <c>%2F</c>.
    /// White space is significant, <c>+</c> is not a space, and <c>.</c> and <c>..</c> are ordinary segments.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, string>? Match(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] pathSegments = SplitSegments(path);
        if (pathSegments.Length != _segments.Length)
            return null;

        for (var i = 0; i < _segments.Length; i++)
        {
            Segment segment = _segments[i];
            if (!segment.IsParameter &&
                !StringComparer.Ordinal.Equals(segment.Text, Uri.UnescapeDataString(pathSegments[i])))
            {
                return null;
            }
        }

        if (_parameterCount == 0)
            return ReadOnlyDictionary<string, string>.Empty;

        var values = new Dictionary<string, string>(_parameterCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _segments.Length; i++)
        {
            Segment segment = _segments[i];
            if (segment.IsParameter)
                values.Add(segment.Text, Uri.UnescapeDataString(pathSegments[i]));
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
    /// are ignored.
    /// </param>
    /// <returns>
    /// The path: <c>/</c> followed by the segments separated by <c>/</c>, with no trailing slash; <c>/</c> for the
    /// root template. Each literal is written as its decoded text and each parameter as its value, both
    /// percent-encoded with <see cref="Uri.EscapeDataString(string)"/>, so a <c>/</c> in a value is written as
    /// <c>%2F</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="Match(string)"/> reads back what this method writes: for every template and every dictionary this
    /// method accepts, matching the formatted path returns exactly the template's parameters, with the values that were
    /// formatted. In the other direction, formatting the values that a match returned produces a path that matches
    /// again with the same values, unless the matched path itself contained a lone surrogate in a parameter's position:
    /// <see cref="Match(string)"/> does not check paths, and this method rejects such a value.
    /// </para>
    /// <para>
    /// That path need not have the same text as the path that was matched. The output is canonical: the same values
    /// always format to the same path, whatever path they were matched from. Every segment is encoded the same way,
    /// whether or not the original text was encoded, so <c>/my files</c> and <c>/my%20files</c> both format as
    /// <c>/my%20files</c>, and repeated slashes are not reproduced.
    /// </para>
    /// <para>
    /// Values are not otherwise checked: white space, control characters, <c>%</c>, <c>/</c> and any other text
    /// are encoded and read back unchanged. <c>.</c> and <c>..</c> are written as they are, as values or literals,
    /// and a URI parser or browser may remove them and the segment before them when the path is resolved.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pathValues"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A parameter has no entry in <paramref name="pathValues"/>; it has none under its name as written and more than
    /// one whose key equals it ignoring case; or its value is <see langword="null"/>, empty, or not valid UTF-16 (it
    /// contains a lone surrogate, which cannot be percent-encoded and read back unchanged). The first parameter in
    /// template order that cannot be formatted is the one reported.
    /// </exception>
    public string Format(IReadOnlyDictionary<string, string> pathValues)
    {
        ArgumentNullException.ThrowIfNull(pathValues);

        if (_segments.Length == 0)
            return "/";

        var builder = new StringBuilder(Value.Length + (16 * _parameterCount));
        foreach (Segment segment in _segments)
        {
            builder.Append('/').Append(
                segment.IsParameter
                    ? Uri.EscapeDataString(GetPathValue(pathValues, segment.Text))
                    : segment.EncodedText);
        }

        return builder.ToString();
    }

    private static string GetPathValue(IReadOnlyDictionary<string, string> pathValues, string name)
    {
        if (!pathValues.TryGetValue(name, out string? value) && !TryGetValueIgnoringCase(pathValues, name, out value))
            throw new ArgumentException($"Path parameter '{name}' has no value.", nameof(pathValues));

        if (value is null)
            throw new ArgumentException($"Path parameter '{name}' has a null value.", nameof(pathValues));

        if (value.Length == 0)
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has an empty value; a parameter always matches exactly one path segment.",
                nameof(pathValues));
        }

        if (ContainsLoneSurrogate(value))
        {
            throw new ArgumentException(
                $"Path parameter '{name}' has a value that is not valid UTF-16: it contains a lone surrogate, " +
                "which cannot be percent-encoded and read back unchanged.",
                nameof(pathValues));
        }

        return value;
    }

    // Only reached when the name as written is not a key, and then it reads every entry. A dictionary that already
    // compares keys without regard to case, such as the one Match returns, finds any spelling of the name on the first
    // lookup, so for it the search runs only when the name is missing, just before the error is thrown.
    private static bool TryGetValueIgnoringCase(
        IReadOnlyDictionary<string, string> pathValues,
        string name,
        out string? value)
    {
        value = null;
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

    // Returns the name between the braces when the segment is a whole-segment parameter, valid or not, and null when
    // the segment is a literal.
    private static string? FindParameterName(string segment)
    {
        if (segment.Length < 2 || segment[0] != '{' || segment[^1] != '}')
            return null;

        ReadOnlySpan<char> name = segment.AsSpan(1, segment.Length - 2);
        return name.ContainsAny('{', '}') ? null : name.ToString();
    }

    private static string? FindSegmentProblem(string segment, string? parameterName)
    {
        // Checked first: a control character is invisible where the template is written, and its rule covers the
        // whole segment, so a tab or line feed at an edge is reported here rather than as white space.
        if (ContainsControlCharacter(segment))
            return "contains a control character";

        // Checked next, for the same reason: a lone surrogate cannot be seen or percent-encoded, and its rule covers
        // the whole segment.
        if (ContainsLoneSurrogate(segment))
            return "contains a lone surrogate, which is not valid UTF-16";

        if (parameterName is not null)
            return FindParameterNameProblem(parameterName);

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

    private static string? FindParameterNameProblem(string name)
    {
        if (name.AsSpan().ContainsAny(':', '?', '*'))
            return "contains ':', '?' or '*', which are not allowed in a parameter name";

        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return InvalidParameterName;

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(name[i]) || name[i] == '_'))
                return InvalidParameterName;
        }

        return null;
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

    private readonly struct Segment
    {
        private Segment(string text, string encodedText, bool isParameter)
        {
            Text = text;
            EncodedText = encodedText;
            IsParameter = isParameter;
        }

        // A literal's decoded text, which path segments are compared with, or a parameter's name.
        public string Text { get; }

        // A literal's text percent-encoded as Format writes it; empty for a parameter.
        public string EncodedText { get; }

        public bool IsParameter { get; }

        public static Segment Literal(string text)
        {
            return new Segment(text, Uri.EscapeDataString(text), false);
        }

        public static Segment Parameter(string name)
        {
            return new Segment(name, string.Empty, true);
        }
    }
}
