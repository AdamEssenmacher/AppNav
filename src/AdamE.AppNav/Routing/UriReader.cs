namespace AdamE.AppNav.Routing;

// Reads the path and the query of a URI as the route table reads them. Match, the context that parses the query, and
// FormatUri, which checks that a link it builds reads back as written, all read here, so that what FormatUri checks
// is exactly what Match will read.
internal static class UriReader
{
    // A relative URI has no parsed parts: AbsolutePath and Query throw for one, so both are read from the text. On
    // Unix-like platforms, new Uri("/settings?tab=1") is an implicit file URI whose AbsolutePath escapes the '?' into
    // the path, so a file URI whose text starts with '/', where the caller wrote no file: scheme, is read from its text
    // too, as the app-relative link it was meant to be.
    public static bool IsReadAsRelative(Uri uri)
    {
        return !uri.IsAbsoluteUri || (uri.IsFile && uri.OriginalString.StartsWith('/'));
    }

    // Where the path ends and where the query starts are decided here together, so that the two always agree. The
    // query itself is not read: queryStart is -1 for an absolute URI, whose query is Uri.Query, and otherwise the index
    // in OriginalString of the '?' that starts it, or the length of the text when there is none, so that a URI that
    // matches nothing, and a route that reads no query values, pay nothing for it.
    public static string ReadPath(Uri uri, out int queryStart)
    {
        if (!IsReadAsRelative(uri))
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

    // The query, with its leading '?' when it has one, from where ReadPath said it starts. The query of a relative URI
    // ends at the first '#' after its '?', and is read from the text in place.
    public static ReadOnlySpan<char> ReadQuery(Uri uri, int queryStart)
    {
        if (queryStart < 0)
            return uri.Query;

        ReadOnlySpan<char> query = uri.OriginalString.AsSpan(queryStart);
        int fragment = query.IndexOf('#');
        return fragment < 0 ? query : query[..fragment];
    }
}
