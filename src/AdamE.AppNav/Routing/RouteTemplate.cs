using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace AdamE.AppNav.Routing;

/// <summary>
/// A parsed route template: the path pattern that a route answers to, such as <c>/settings/profile</c>.
/// </summary>
/// <remarks>
/// <para>
/// A template is a path that starts with <c>/</c> and is divided into segments by <c>/</c>. Every segment is a
/// literal that a path must contain exactly, in the same position.
/// </para>
/// <para>
/// Templates and paths are divided the same way. Leading, trailing and repeated slashes do not create segments, so
/// <c>/a//b/</c> has the same segments as <c>/a/b</c>, and <c>/</c> is the root template, which has no segments.
/// Each segment is then percent-decoded once, so <c>%2F</c> is part of its segment and does not divide it. A percent
/// sequence that is not valid, or does not decode to valid UTF-8, is kept as written.
/// </para>
/// <para>
/// A template segment cannot contain a control character (one for which <see cref="char.IsControl(char)"/> is
/// <see langword="true"/>, such as a tab or line feed), <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>, or start or end
/// with white space. To match those characters, percent-encode them in the template: for example <c>%09</c> for a
/// tab, <c>%0A</c> for a line feed, <c>%00</c> for a null character, <c>%7B</c>, <c>%7D</c>, <c>%3F</c>, <c>%23</c>
/// and <c>%20</c>.
/// </para>
/// </remarks>
public sealed class RouteTemplate
{
    private readonly string[] _segments;

    private RouteTemplate(string value, string[] segments)
    {
        Value = value;
        _segments = segments;
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
    /// <paramref name="value"/> is empty or consists only of white space, does not start with <c>/</c>, or has a
    /// segment that contains a control character, <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>, or starts or ends with
    /// white space. These checks apply to the text as written, before percent-decoding. A segment with a control
    /// character is reported as such even when the character is also white space, such as a tab at the start, and the
    /// message shows each control character as a <c>\uXXXX</c> escape.
    /// </exception>
    public static RouteTemplate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!value.StartsWith('/'))
            throw new ArgumentException("Route templates must start with '/'.", nameof(value));

        string[] segments = SplitSegments(value);
        foreach (string segment in segments)
        {
            if (FindSegmentProblem(segment) is { } problem)
            {
                throw new ArgumentException(
                    $"Route template segment '{EscapeControlCharacters(segment)}' {problem}.",
                    nameof(value));
            }
        }

        return new RouteTemplate(value, DecodeSegments(segments));
    }

    /// <summary>
    /// Matches a path against this template.
    /// </summary>
    /// <param name="path">
    /// The path to match, such as <c>/settings/profile</c>. A leading <c>/</c> is optional. The whole value is the
    /// path: it is not trimmed, and a query string or fragment is not removed.
    /// </param>
    /// <returns>
    /// An empty, read-only dictionary when the path has the same number of segments as the template and each decoded
    /// segment equals the template's segment in the same position; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Segments are compared ordinally and case-sensitively. White space is significant, <c>+</c> is not a space,
    /// and <c>.</c> and <c>..</c> are ordinary segments.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, string>? Match(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] pathSegments = DecodeSegments(SplitSegments(path));
        if (pathSegments.Length != _segments.Length)
            return null;

        for (var i = 0; i < _segments.Length; i++)
        {
            if (!StringComparer.Ordinal.Equals(_segments[i], pathSegments[i]))
                return null;
        }

        return ReadOnlyDictionary<string, string>.Empty;
    }

    private static string? FindSegmentProblem(string segment)
    {
        // Checked first: a control character is invisible where the template is written, and its rule covers the
        // whole segment, so a tab or line feed at an edge is reported here rather than as white space.
        if (ContainsControlCharacter(segment))
            return "contains a control character";

        if (segment.Contains('{', StringComparison.Ordinal) || segment.Contains('}', StringComparison.Ordinal))
            return "contains '{' or '}'";

        if (segment.Contains('?', StringComparison.Ordinal) || segment.Contains('#', StringComparison.Ordinal))
            return "contains '?' or '#'";

        if (char.IsWhiteSpace(segment[0]) || char.IsWhiteSpace(segment[^1]))
            return "starts or ends with white space";

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

    // Writes each control character as a \uXXXX escape, so that quoting a segment in a message cannot break the line
    // or the terminal it is printed to. Other characters are quoted as written.
    private static string EscapeControlCharacters(string segment)
    {
        if (!ContainsControlCharacter(segment))
            return segment;

        var builder = new StringBuilder(segment.Length + 8);
        foreach (char c in segment)
        {
            if (char.IsControl(c))
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

    private static string[] DecodeSegments(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
            segments[i] = Uri.UnescapeDataString(segments[i]);

        return segments;
    }
}
