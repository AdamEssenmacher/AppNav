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

    // The name is already percent-encoded; the value is encoded with Uri.EscapeDataString, which leaves only ASCII
    // letters, digits and '-', '.', '_' and '~' as they are: a space becomes %20 and '+' becomes %2B, because the query
    // reader takes '+' for a space, and '&', '=', '#', '?' and '%' are encoded, so that the reader splits the pairs
    // where they were joined and decodes each name and value back to the text written. Uri.EscapeDataString writes a
    // lone surrogate as the encoding of U+FFFD, so the value would be read back changed; it is refused, as a path value
    // with one is, and false is returned with nothing appended.
    public bool TryAppend(string encodedName, string value)
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

    // The path followed by the query, or the path alone when no pair was appended.
    public override readonly string ToString()
    {
        return _builder?.ToString() ?? _path;
    }
}
