using System.Collections.ObjectModel;

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
/// A template segment cannot contain <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>, or start or end with white space.
/// To match those characters, percent-encode them in the template: <c>%7B</c>, <c>%7D</c>, <c>%3F</c>, <c>%23</c>
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
    /// segment that contains <c>{</c>, <c>}</c>, <c>?</c> or <c>#</c>, or starts or ends with white space. These
    /// checks apply to the text as written, before percent-decoding.
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
                throw new ArgumentException($"Route template segment '{segment}' {problem}.", nameof(value));
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
        if (segment.Contains('{', StringComparison.Ordinal) || segment.Contains('}', StringComparison.Ordinal))
            return "contains '{' or '}'";

        if (segment.Contains('?', StringComparison.Ordinal) || segment.Contains('#', StringComparison.Ordinal))
            return "contains '?' or '#'";

        if (char.IsWhiteSpace(segment[0]) || char.IsWhiteSpace(segment[^1]))
            return "starts or ends with white space";

        return null;
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
