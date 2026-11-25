using System.Collections.ObjectModel;

namespace AdamE.AppNav.Routing;

// Reads the query part of a URI as form-encoded names, each with its values in the order they appear, so that the
// route table can bind query parameters. The rules follow how browsers read a query, except where they keep it
// consistent with how paths are read here: a percent sequence that is not valid, or not valid UTF-8, is kept as
// written rather than replaced with U+FFFD, and names are compared without regard to case.
internal static class QueryString
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoPairs =
        ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;

    // Splits the query into pairs and groups their values by name:
    // - One leading '?' is removed, so both Uri.Query and a bare query are accepted; a second '?' belongs to the name.
    // - Nothing is trimmed, and '#' is not special: the caller removes a fragment first, as for a path.
    // - Pairs are separated by '&' only, not ';', and empty pairs are skipped.
    // - The first '=' separates the name from the value, which is empty when there is no '='. An empty name is kept.
    // - The name and the value are decoded separately, after splitting, so an encoded '&' or '=' is data: '+' is a
    //   space, as in form encoding, and percent sequences are decoded once, keeping one that is not valid, or not
    //   valid UTF-8, as written, exactly as for a path segment. The decoded text is not checked.
    // - Names are compared ordinally ignoring case, as path parameter names are; the dictionary looks them up the
    //   same way, and its key is the spelling of a name's first occurrence. Each name's values keep their order,
    //   including repeated and empty values. The order of the names themselves is not defined.
    // The query is a span, so that the query of a relative URI is parsed in place rather than copied first. Returns
    // one shared empty dictionary when there are no pairs, and otherwise a new one. Treat the dictionary and its value
    // lists as read-only, as with the values RouteTemplate.Match returns.
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(ReadOnlySpan<char> query)
    {
        ReadOnlySpan<char> pairs = query.StartsWith('?') ? query[1..] : query;
        Dictionary<string, IReadOnlyList<string>>? values = null;
        foreach (Range range in pairs.Split('&'))
        {
            ReadOnlySpan<char> pair = pairs[range];
            if (pair.IsEmpty)
                continue;

            int separator = pair.IndexOf('=');
            string name = Decode(separator < 0 ? pair : pair[..separator]);
            string value = separator < 0 ? string.Empty : Decode(pair[(separator + 1)..]);

            values ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            if (!values.TryGetValue(name, out IReadOnlyList<string>? existing))
            {
                // Most names appear once, so their array is allocated at its final length.
                values.Add(name, new[] { value });
            }
            else if (existing is List<string> list)
            {
                list.Add(value);
            }
            else
            {
                // A repeated name moves to a List, so that many repeats cost linear time rather than a copy per value.
                values[name] = new List<string> { existing[0], value };
            }
        }

        return values ?? NoPairs;
    }

    // '+' becomes a space before percent-decoding, so that '%2B' still decodes to a literal '+'. Text without '+' is
    // decoded straight from the span, which allocates only the result.
    private static string Decode(ReadOnlySpan<char> text)
    {
        if (!text.Contains('+'))
            return Uri.UnescapeDataString(text);

        string spaced = string.Create(text.Length, text, static (buffer, source) => source.Replace(buffer, '+', ' '));
        return Uri.UnescapeDataString(spaced);
    }
}
