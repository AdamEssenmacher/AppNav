using System.Globalization;
using System.Text;

namespace AdamE.AppNav.Routing;

// The text rules that parsing, matching and formatting share: how a template or a path divides into segments, and
// which characters cannot be written, percent-encoded or quoted in a message as they are, namely control characters
// and lone surrogates. Kept in one place so that templates and paths divide alike and every message quotes alike.
internal static class RouteText
{
    public static string[] SplitSegments(string path)
    {
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool ContainsLoneSurrogate(string value)
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

    public static bool ContainsControlCharacter(string text)
    {
        foreach (char c in text)
        {
            if (char.IsControl(c))
                return true;
        }

        return false;
    }

    // Writes each control character and each lone surrogate as a \uXXXX escape, so that quoting text in a message
    // cannot break the line or the terminal it is printed to, or produce text that is not valid UTF-16. Other
    // characters, including valid surrogate pairs, are quoted as written.
    public static string EscapeForMessage(string text)
    {
        if (!ContainsControlCharacter(text) && !ContainsLoneSurrogate(text))
            return text;

        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                builder.Append(c).Append(text[++i]);
            else if (char.IsControl(c) || char.IsSurrogate(c))
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            else
                builder.Append(c);
        }

        return builder.ToString();
    }
}
