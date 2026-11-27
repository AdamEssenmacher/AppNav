using System.Diagnostics;
using System.Text;

namespace AdamE.AppNav.Routing;

// The query of one formatted route, appended to its path one name=value pair at a time. It is a struct passed by
// reference, and its StringBuilder is made only for the first pair, so a route whose query writes nothing costs nothing
// beyond its path, which is returned as it is. Every pair of every declaration goes into the one builder, so a long
// list is written in linear time.
internal struct QueryBuilder
{
    private readonly string _path;
    private StringBuilder? _builder;

    public QueryBuilder(string path)
    {
        _path = path;
    }

    // Returns a name percent-encoded as every pair writes it, or throws for one that cannot be written: one with a lone
    // surrogate, which Uri.EscapeDataString would write as the encoding of U+FFFD, so that it would read back changed.
    // Every declaration of a query name checks it here, so that each applies the same rule; what says which name it
    // is, as in "query value name", and the message quotes the name escaped.
    public static string EncodeName(string name, string what, string paramName)
    {
        if (RouteText.ContainsLoneSurrogate(name))
        {
            throw new ArgumentException(
                $"The {what} '{RouteText.EscapeForMessage(name)}' is not valid UTF-16: it contains a lone " +
                "surrogate, which cannot be percent-encoded and read back unchanged.",
                paramName);
        }

        return Uri.EscapeDataString(name);
    }

    // Appends the pair for one value that a writer has turned into text, or says why it cannot: the problem the
    // writer found, None with nothing appended for a value that is null and so left out, or a lone surrogate in the
    // text. Every writer of a query pair, a single query value, an element of a list and a route metadata value
    // alike, appends through here, so that each reports a value that cannot be written in the same way.
    public QueryValueProblem AppendValue(string encodedName, ValueTextProblem problem, string? text)
    {
        if (problem != ValueTextProblem.None)
            return ProblemOf(problem);

        if (text is null)
            return QueryValueProblem.None;

        return TryAppend(encodedName, text) ? QueryValueProblem.None : QueryValueProblem.LoneSurrogate;
    }

    // The name is already percent-encoded; the value is encoded with Uri.EscapeDataString, which leaves only ASCII
    // letters, digits and '-', '.', '_' and '~' as they are: a space becomes %20 and '+' becomes %2B, because the query
    // reader takes '+' for a space, and '&', '=', '#', '?' and '%' are encoded, so that the reader splits the pairs
    // where they were joined and decodes each name and value back to the text written. Uri.EscapeDataString writes a
    // lone surrogate as the encoding of U+FFFD, so the value would be read back changed; it is refused, as a path value
    // with one is, and false is returned with nothing appended.
    private bool TryAppend(string encodedName, string value)
    {
        if (RouteText.ContainsLoneSurrogate(value))
            return false;

        if (_builder is null)
            _builder = new StringBuilder(_path.Length + 64).Append(_path).Append('?');
        else
            _builder.Append('&');

        _builder.Append(encodedName).Append('=').Append(Uri.EscapeDataString(value));
        return true;
    }

    // The problem of the query for a value that could not be written.
    private static QueryValueProblem ProblemOf(ValueTextProblem problem)
    {
        return problem switch
        {
            ValueTextProblem.NoText => QueryValueProblem.NoText,
            ValueTextProblem.NotReadBack => QueryValueProblem.NotReadBack,
            _ => throw new UnreachableException("A value writer reported a problem that the query has no name for."),
        };
    }

    // The path followed by the query, or the path alone when no pair was appended.
    public override readonly string ToString()
    {
        return _builder?.ToString() ?? _path;
    }
}
